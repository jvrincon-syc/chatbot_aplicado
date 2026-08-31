using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Api;
using Chatbot.Sst.Domain;
using Chatbot.Sst.Infrastructure;
using Chatbot.Sst.Infrastructure.Llm;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<LlmHealthCheck>("llm", tags: ["llm"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = HealthEndpointPredicates.IncludeForLiveness
});
app.MapHealthChecks("/health/llm", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = HealthEndpointPredicates.IncludeLlmOnly
});

if (app.Environment.IsDevelopment())
{
    app.MapPost("/dev/llm/smoke", async (ILlmProvider llm, CancellationToken ct) =>
    {
        var request = new LlmRequest([
            new LlmMessage(LlmRole.System, "You are a test harness. Reply with a single short word."),
            new LlmMessage(LlmRole.User, "Say: ok")
        ])
        {
            MaxOutputTokens = 8,
            Temperature = 0
        };

        var response = await llm.GenerateAsync(request, ct);
        return Results.Ok(new { output = response.Content });
    });
}

app.MapPost("/api/chat/requests", async (StartChatRequest body, IChatDispatchCoordinator coordinator, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.Question))
    {
        return Results.BadRequest(new { error = "Question is required." });
    }

    if (body.TopK is < 1 or > 25)
    {
        return Results.BadRequest(new { error = "TopK must be between 1 and 25." });
    }

    var snapshot = await coordinator.SubmitAsync(body.ToDomain(), ct);
    if (snapshot.State == ChatRequestState.Failed)
    {
        return Results.Json(
            new { requestId = snapshot.RequestId, errorCode = snapshot.ErrorCode, error = snapshot.Error },
            statusCode: StatusCodes.Status502BadGateway);
    }

    return Results.Accepted($"/api/chat/requests/{snapshot.RequestId}", ChatRequestStatusResponse.From(snapshot));
});

app.MapGet("/api/chat/requests/{requestId}", (string requestId, IChatDispatchCoordinator coordinator) =>
{
    var snapshot = coordinator.Get(requestId);
    return snapshot is null
        ? Results.NotFound(new { error = "Request not found." })
        : Results.Ok(ChatRequestStatusResponse.From(snapshot));
});

// Server-Sent Events: streams answer.delta.v1 tokens as the LLM produces them, then a terminal
// answer.completed.v1 / request.failed.v1. Replaces the frontend's 1s polling loop.
app.MapGet("/api/chat/requests/{requestId}/events", async (
    string requestId,
    IChatEventStream events,
    HttpResponse response,
    CancellationToken ct) =>
{
    response.Headers.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";
    response.Headers.Append("X-Accel-Buffering", "no");

    await foreach (var evt in events.SubscribeAsync(requestId, ct))
    {
        await response.WriteAsync($"event: {evt.EventType}\ndata: {evt.DataJson}\n\n", ct);
        await response.Body.FlushAsync(ct);
        if (evt.IsTerminal)
        {
            break;
        }
    }
});

app.MapPost("/api/chat/webhook", async (ChatWebhookRequest body, IChatDispatchCoordinator coordinator, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.DispatchId) ||
        string.IsNullOrWhiteSpace(body.ProjectId) ||
        string.IsNullOrWhiteSpace(body.RagVariantId) ||
        string.IsNullOrWhiteSpace(body.RagReleaseId) ||
        string.IsNullOrWhiteSpace(body.RetrievalProfileId) ||
        string.IsNullOrWhiteSpace(body.Question))
    {
        return Results.BadRequest(new
        {
            error = "dispatch_id, project_id, rag_variant_id, rag_release_id, retrieval_profile_id, and question are required."
        });
    }

    var snapshot = await coordinator.CompleteAsync(body.ToDomain(), ct);
    if (snapshot is null)
    {
        return Results.NotFound(new { error = "Unknown dispatch_id or message_id." });
    }

    if (snapshot.State == ChatRequestState.Failed)
    {
        return Results.Json(
            new { requestId = snapshot.RequestId, errorCode = snapshot.ErrorCode, error = snapshot.Error },
            statusCode: StatusCodes.Status502BadGateway);
    }

    return Results.Accepted($"/api/chat/requests/{snapshot.RequestId}", ChatRequestStatusResponse.From(snapshot));
});

app.MapGet("/api/chat/rag-releases", async (IChatbotDispatchClient dispatchClient, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        var releases = await dispatchClient.ListRagReleasesAsync(ct);
        return Results.Ok(releases.Select(RagReleaseResponse.From).ToArray());
    }
    catch (ChatDispatchException ex)
    {
        return Results.Json(
            new { errorCode = ex.ErrorCode, error = ex.Message },
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception ex)
    {
        // Fail closed: an unreachable/unexpected chatbot-sst failure here (e.g. connection
        // refused) must not leak a raw exception to callers, same as the background dispatch
        // path in ChatDispatchCoordinator.
        logger.LogError(ex, "Unexpected failure listing RAG releases from the external chatbot backend.");
        return Results.Json(
            new { errorCode = "CHATBOT_RAG_RELEASES_UNEXPECTED_FAILURE", error = "No se pudieron consultar las releases RAG disponibles." },
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

public partial class Program;
