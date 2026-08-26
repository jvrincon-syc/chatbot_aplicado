using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Application.Generation;
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

// Liveness + LLM reachability.
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/llm", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("llm")
});

// Dev-only smoke path: proves API -> ILlmProvider -> llama.cpp -> Qwen. NOT the chat orchestration.
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

// Chat endpoint the React client calls. Receives the question + its N evidence fragments and passes
// them to the grounded generation use case. Fail-closed: no fragments ⇒ abstention, no LLM call.
// ponytail: fragments arrive from the caller for now; when IRagRetriever is implemented the API will
// retrieve them server-side from the RAG (Postgres) instead of receiving them in the request body.
app.MapPost("/api/chat", async (ChatRequest body, IChatService chat, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.Message))
    {
        return Results.BadRequest(new { error = "Message is required." });
    }

    var evidence = body.ToEvidencePackage();
    var response = await chat.AnswerAsync(new UserQuestion(body.Message), evidence, ct);
    return Results.Ok(response);
});

app.Run();

/// <summary>Question + the N evidence fragments supplied for grounding.</summary>
public sealed record ChatRequest(string Message, IReadOnlyList<EvidenceFragment>? Fragments = null)
{
    public EvidencePackage ToEvidencePackage()
    {
        var items = (Fragments ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f.Content))
            .Select(f => new Evidence(
                f.Content,
                new Citation(f.DocumentId ?? "unknown", f.DocumentTitle, f.Page, f.Section),
                f.Score ?? 0))
            .ToArray();

        if (items.Length == 0) return EvidencePackage.Empty;

        var tokens = items.Sum(i => EvidencePromptBuilder.EstimateTokens(i.Content));
        return new EvidencePackage(items, tokens);
    }
}

public sealed record EvidenceFragment(
    string Content,
    string? DocumentId = null,
    string? DocumentTitle = null,
    string? Page = null,
    string? Section = null,
    double? Score = null);

public partial class Program;
