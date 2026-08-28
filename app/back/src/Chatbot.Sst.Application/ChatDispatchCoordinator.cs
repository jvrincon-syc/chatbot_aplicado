using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Application.Generation;
using Chatbot.Sst.Domain;
using Microsoft.Extensions.Logging;

namespace Chatbot.Sst.Application;

/// <summary>
/// Orchestrates the reduced chatbot scope: submit the question to the external backend, receive the
/// webhook with chunks, and ask the local LLM only from those chunks.
/// </summary>
public sealed class ChatDispatchCoordinator : IChatDispatchCoordinator
{
    private const string LlmDeliveryFailedCode = "CHATBOT_LLM_DELIVERY_FAILED";
    private const string LlmDeliveryFailedMessage =
        "No se pudo generar la respuesta final con el LLM local.";
    private const string LlmUnavailableCode = "CHATBOT_LLM_UNAVAILABLE";
    private const string LlmUnavailableMessage =
        "El modelo local no está disponible en este momento.";
    private const string LlmTimeoutCode = "CHATBOT_LLM_TIMEOUT";
    private const string LlmTimeoutMessage =
        "El modelo local tardó demasiado en responder.";
    private const string DispatchUnexpectedFailedCode = "CHATBOT_DISPATCH_UNEXPECTED_FAILURE";
    private const string DispatchUnexpectedFailedMessage =
        "Fallo inesperado al despachar la pregunta al backend de contexto.";

    // Prefill (prompt processing) of the evidence block dominates local-LLM latency — measured
    // 77% of total generation time — and scales roughly linearly with prompt size. Retrieval's
    // topK (5-10) is already reranked by relevance, so keeping only the highest-scored chunks
    // trims prefill cost while preserving enough source diversity for a grounded, multi-source
    // answer.
    private const int MaxEvidenceItems = 5;

    private readonly IChatRequestStore _store;
    private readonly IChatbotDispatchClient _dispatchClient;
    private readonly IChatService _chat;
    private readonly ILogger<ChatDispatchCoordinator> _logger;

    public ChatDispatchCoordinator(
        IChatRequestStore store,
        IChatbotDispatchClient dispatchClient,
        IChatService chat,
        ILogger<ChatDispatchCoordinator> logger)
    {
        _store = store;
        _dispatchClient = dispatchClient;
        _chat = chat;
        _logger = logger;
    }

    public Task<ChatRequestSnapshot> SubmitAsync(ChatQuestionSubmission submission, CancellationToken cancellationToken)
    {
        var pending = _store.CreatePending(submission);
        var prepared = submission with { MessageId = pending.RequestId };

        // The external backend's DispatchAsync call is synchronous end-to-end: it blocks
        // on Python's retrieval, Python's own callback into our /api/chat/webhook, and the
        // local LLM generation inside that callback — routinely 90-150s. Firing it here and
        // returning the pending snapshot immediately lets /api/chat/requests respond in
        // milliseconds; callers already poll GET /api/chat/requests/{id} for the result, so
        // this removes the need for the outer HTTP call's timeout to out-wait the whole
        // nested chain (the exact shape of bug that forced RequestTimeoutSeconds to 210s).
        // CancellationToken.None is deliberate: `cancellationToken` here is the inbound
        // request's RequestAborted token, which fires the moment we return below and would
        // otherwise cancel the in-flight dispatch immediately after the client gets its 202.
        _ = DispatchInBackgroundAsync(pending.RequestId, prepared);

        return Task.FromResult(pending);
    }

    private async Task DispatchInBackgroundAsync(string requestId, ChatQuestionSubmission prepared)
    {
        try
        {
            var receipt = await _dispatchClient.DispatchAsync(prepared, CancellationToken.None);
            _store.AttachDispatchReceipt(requestId, receipt);
        }
        catch (ChatDispatchException ex)
        {
            _store.Fail(requestId, ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            // Background task: nothing observes this Task, so an uncaught exception would
            // silently strand the request in Pending forever instead of surfacing as a
            // 502 to the poller. Log and fail the request explicitly.
            _logger.LogError(
                ex,
                "Unexpected failure dispatching chat request {RequestId} to the external chatbot backend.",
                requestId);
            _store.Fail(requestId, DispatchUnexpectedFailedCode, DispatchUnexpectedFailedMessage);
        }
    }

    public ChatRequestSnapshot? Get(string requestId) => _store.Get(requestId);

    public Task<ChatRequestSnapshot?> CompleteAsync(ChatWebhookDelivery delivery, CancellationToken cancellationToken)
    {
        var existing = !string.IsNullOrWhiteSpace(delivery.MessageId)
            ? _store.Get(delivery.MessageId)
            : null;
        existing ??= _store.GetByDispatchId(delivery.DispatchId);
        if (existing is null)
        {
            return Task.FromResult<ChatRequestSnapshot?>(null);
        }

        var attached = _store.AttachChunks(existing.RequestId, delivery);

        // Same reasoning as SubmitAsync's DispatchInBackgroundAsync: _chat.AnswerAsync is a full
        // local-LLM generation call (20-55s). Awaiting it here would block the webhook's HTTP
        // response to the Python caller, which is exactly the latency this fixes. Chunks are
        // already persisted above, so the response below reflects that immediately; generation
        // finishes in the background and pollers pick it up via GET /api/chat/requests/{id}.
        // CancellationToken.None is deliberate: `cancellationToken` here is the inbound webhook
        // request's RequestAborted token, which fires the moment we return below.
        _ = GenerateInBackgroundAsync(existing.RequestId, delivery);

        return Task.FromResult(attached);
    }

    private async Task GenerateInBackgroundAsync(string requestId, ChatWebhookDelivery delivery)
    {
        try
        {
            var evidence = ToEvidencePackage(delivery.Chunks);
            var response = await _chat.AnswerAsync(new UserQuestion(delivery.Question), evidence, CancellationToken.None);
            _store.Complete(requestId, delivery, response);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            var (code, message) = ClassifyLlmFailure(ex);
            _logger.LogError(
                ex,
                "LLM generation failed for chat request {RequestId} (dispatch_id={DispatchId}): " +
                "classified as {ErrorCode} from {ExceptionType}.",
                requestId,
                delivery.DispatchId,
                code,
                ex.GetType().Name);
            _store.Fail(requestId, code, message, delivery);
        }
    }

    /// <summary>
    /// Maps the exception shapes thrown by ILlmProvider into a client-safe error code/message pair.
    /// Uses typed signals (HttpRequestException.StatusCode is null on connection failures, set on
    /// non-2xx responses) rather than string-matching exception messages.
    /// </summary>
    private static (string Code, string Message) ClassifyLlmFailure(Exception ex) => ex switch
    {
        // No response was ever received: connection refused, DNS failure, server process down.
        HttpRequestException { StatusCode: null } => (LlmUnavailableCode, LlmUnavailableMessage),
        // CancellationToken.None is passed to AnswerAsync above, so a TaskCanceledException here
        // can only originate from HttpClient's own request timeout, never user cancellation.
        TaskCanceledException => (LlmTimeoutCode, LlmTimeoutMessage),
        // Non-2xx response from a reachable server, or a malformed/empty response body
        // (InvalidOperationException from OpenAiCompatibleLlmProvider) — generic delivery failure.
        _ => (LlmDeliveryFailedCode, LlmDeliveryFailedMessage)
    };

    // Applied once here so the prompt (EvidencePromptBuilder) and the citations
    // (GroundedAnswerService, built from the same EvidencePackage.Items) always agree on
    // exactly what the LLM saw — never cite a chunk that was trimmed before reaching the prompt.
    private EvidencePackage ToEvidencePackage(IReadOnlyList<WebhookChunk> chunks)
    {
        var items = chunks
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk.Text))
            .Select(chunk => chunk.ToEvidence())
            .ToArray();

        if (items.Length == 0)
        {
            return EvidencePackage.Empty;
        }

        // Sort defensively by score rather than trusting upstream order, then keep only the
        // top N. Ties keep their relative input order (OrderByDescending is stable) — fine
        // since retrieval already ranks reasonably; nothing here needs to be smarter than that.
        var kept = items.Length > MaxEvidenceItems
            ? items.OrderByDescending(item => item.Score).Take(MaxEvidenceItems).ToArray()
            : items;

        var tokens = kept.Sum(item => EvidencePromptBuilder.EstimateTokens(item.Content));

        if (kept.Length != items.Length)
        {
            var beforeTokens = items.Sum(item => EvidencePromptBuilder.EstimateTokens(item.Content));
            _logger.LogInformation(
                "Evidence trimmed from {BeforeCount} to {AfterCount} chunks by score " +
                "(estimated tokens {BeforeTokens} -> {AfterTokens}).",
                items.Length, kept.Length, beforeTokens, tokens);
        }

        return new EvidencePackage(kept, tokens);
    }
}
