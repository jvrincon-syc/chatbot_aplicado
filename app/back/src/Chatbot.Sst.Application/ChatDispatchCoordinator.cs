using System.Text.Json;
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
    // 77% of total generation time — and scales roughly linearly with prompt size. Two guards
    // bound it: at most MaxEvidenceItems chunks AND at most EvidenceTokenBudget estimated tokens,
    // so four huge chunks can't still produce a huge prefill. Retrieval already reranks by
    // relevance, so keeping the highest-scored chunks preserves grounding. This is a hard cap:
    // the token budget only ever selects fewer than MaxEvidenceItems, never more.
    private const int MaxEvidenceItems = 5;

    private const string CodeRefusalMessage =
        "Solo puedo ayudarte con preguntas sobre los documentos de seguridad y salud en el trabajo " +
        "(SST) de la empresa. No genero código, scripts ni algoritmos. ¿En qué tema de SST te puedo ayudar?";

    private const string LowRelevanceMessage =
        "No encontré información suficiente en los documentos de SST de la empresa para responder eso. " +
        "¿Puedo ayudarte con otra pregunta sobre seguridad y salud en el trabajo?";

    private const string DeltaEventType = "chat.answer.delta.v1";
    private const string CompletedEventType = "chat.answer.completed.v1";
    private const string FailedEventType = "chat.request.failed.v1";

    private readonly IChatRequestStore _store;
    private readonly IChatbotDispatchClient _dispatchClient;
    private readonly IChatService _chat;
    private readonly IChatEventStream _events;
    private readonly GenerationOptions _generation;
    private readonly ILogger<ChatDispatchCoordinator> _logger;

    public ChatDispatchCoordinator(
        IChatRequestStore store,
        IChatbotDispatchClient dispatchClient,
        IChatService chat,
        IChatEventStream events,
        GenerationOptions generation,
        ILogger<ChatDispatchCoordinator> logger)
    {
        _store = store;
        _dispatchClient = dispatchClient;
        _chat = chat;
        _events = events;
        _generation = generation;
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
            await FailAndPublishAsync(requestId, ex.ErrorCode, ex.Message);
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
            await FailAndPublishAsync(requestId, DispatchUnexpectedFailedCode, DispatchUnexpectedFailedMessage);
        }
    }

    // A dispatch-stage failure (backend 500/OOM, connection refused) happens after SubmitAsync
    // already returned Pending and the browser opened its SSE subscription. Marking the store
    // Failed is not enough: without a terminal event on the stream the SSE waits forever and the
    // UI hangs. Publish the failed event so the open stream — and any late/reconnecting subscriber,
    // which replays from the start — settles instead of hanging.
    private async Task FailAndPublishAsync(string requestId, string errorCode, string message)
    {
        _store.Fail(requestId, errorCode, message);
        await PublishAsync(
            requestId, FailedEventType,
            JsonSerializer.Serialize(new { errorCode, error = message }), terminal: true);
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
        // FAQ fast path: a confident FAQ hit comes back as a single chunk the retrieval tagged
        // embedding_profile_id="faq". Its text is already the curated final answer, so return it
        // verbatim and skip the LLM entirely — the whole point of the FAQ shortcut is a sub-second
        // reply instead of ~14s of local generation. No try/catch: there is no external call to fail.
        var faqResponse = TryBuildFaqResponse(delivery);
        if (faqResponse is not null)
        {
            _store.Complete(requestId, delivery, faqResponse);
            await PublishAsync(requestId, CompletedEventType, SerializeCompleted(faqResponse), terminal: true);
            return;
        }

        // Code-request hard block: the small local model caves to "write me a script" under SST
        // framing despite the system prompt, so refuse deterministically before generation. No LLM,
        // no citations — a fixed scope message the model cannot be talked out of.
        if (CodeRequestDetector.IsCodeRequest(delivery.Question))
        {
            var refusal = new ChatResponse(CodeRefusalMessage, [], Abstained: false);
            _store.Complete(requestId, delivery, refusal);
            await PublishAsync(requestId, CompletedEventType, SerializeCompleted(refusal), terminal: true);
            return;
        }

        // Relevance gate: log the best rerank score for every request (so the threshold can be tuned
        // against real traffic) and, when configured, abstain deterministically below it instead of
        // letting the small model improvise from loosely-related evidence.
        var maxScore = delivery.Chunks.Count > 0 ? delivery.Chunks.Max(c => c.Score) : double.NegativeInfinity;
        _logger.LogInformation(
            "Evidence max rerank score {Score} ({ChunkCount} chunks) for chat request {RequestId}: {Question}",
            maxScore, delivery.Chunks.Count, requestId, delivery.Question);
        if (_generation.MinEvidenceScore is { } minScore && maxScore < minScore)
        {
            _logger.LogInformation(
                "Abstaining (score {Score} < MinEvidenceScore {Threshold}) for chat request {RequestId}.",
                maxScore, minScore, requestId);
            var lowRelevance = new ChatResponse(LowRelevanceMessage, [], Abstained: true);
            _store.Complete(requestId, delivery, lowRelevance);
            await PublishAsync(requestId, CompletedEventType, SerializeCompleted(lowRelevance), terminal: true);
            return;
        }

        try
        {
            var evidence = ToEvidencePackage(delivery.Chunks);
            ChatResponse? final = null;

            // The model the user picked in the frontend was stored on the pending request; use it
            // for this generation (null => provider default).
            var modelId = _store.Get(requestId)?.ModelId;

            // Stream token-by-token: each delta reaches the browser over SSE as it is produced
            // (TTFT ~= prefill), then the terminal event carries the full answer + citations.
            await foreach (var chunk in _chat.AnswerStreamingAsync(
                new UserQuestion(delivery.Question), evidence, CancellationToken.None, modelId))
            {
                if (chunk.IsFinal)
                {
                    final = chunk.Final;
                    break;
                }

                await PublishAsync(
                    requestId, DeltaEventType,
                    JsonSerializer.Serialize(new { delta = chunk.Delta }), terminal: false);
            }

            // Defensive: a stream that ends without a final chunk is treated as an abstention
            // rather than persisting a half-answer.
            final ??= ChatResponse.Abstention();
            _store.Complete(requestId, delivery, final);
            await PublishAsync(requestId, CompletedEventType, SerializeCompleted(final), terminal: true);
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
            await PublishAsync(
                requestId, FailedEventType,
                JsonSerializer.Serialize(new { errorCode = code, error = message }), terminal: true);
        }
    }

    // Best-effort: the store already holds the authoritative state (poll fallback), so a Redis
    // publish failure degrades SSE to polling rather than failing the whole generation.
    private async Task PublishAsync(string requestId, string eventType, string json, bool terminal)
    {
        try
        {
            await _events.PublishAsync(requestId, new ChatStreamEvent(eventType, json, terminal), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish {EventType} for chat request {RequestId} to the event stream.", eventType, requestId);
        }
    }

    private static string SerializeCompleted(ChatResponse response) => JsonSerializer.Serialize(new
    {
        answer = response.Answer,
        abstained = response.Abstained,
        citations = response.Citations.Select(c => new
        {
            documentId = c.DocumentId,
            documentTitle = c.DocumentTitle,
            page = c.Page,
            section = c.Section,
            sourceUrl = c.SourceUrl,
        }),
    });

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

    // Detects a FAQ hit and turns it into the final answer without the LLM. The retrieval side
    // returns exactly one chunk tagged embedding_profile_id="faq" whose Text is the curated answer.
    private static ChatResponse? TryBuildFaqResponse(ChatWebhookDelivery delivery)
    {
        if (delivery.Chunks.Count != 1)
        {
            return null;
        }

        var chunk = delivery.Chunks[0];
        if (!string.Equals(chunk.EmbeddingProfileId, "faq", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var answer = chunk.Text?.Trim();
        if (string.IsNullOrEmpty(answer))
        {
            return null;
        }

        // The curated FAQ set marks unanswerable entries with a fail-closed status; honour it so the
        // UI renders them as an abstention rather than a confident answer.
        var status = ReadMetadata(chunk, "faq_status");
        var abstained = status is "insufficient_evidence" or "conflicting_evidence";

        // Synthetic FAQ ids (greetings with no backing document) would make noisy citations; only
        // cite a hit that carries a real document identity.
        IReadOnlyList<Citation> citations = chunk.DocumentId.StartsWith("FAQ-", StringComparison.OrdinalIgnoreCase)
            ? []
            : [chunk.ToEvidence().Citation];

        return new ChatResponse(answer, citations, abstained);
    }

    private static string? ReadMetadata(WebhookChunk chunk, string key)
    {
        if (chunk.Metadata is null)
        {
            return null;
        }

        return chunk.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    // Applied once here so the prompt (EvidencePromptBuilder) and the citations
    // (GroundedAnswerService, built from the same EvidencePackage.Items) always agree on
    // exactly what the LLM saw — never cite a chunk that was trimmed before reaching the prompt.
    private EvidencePackage ToEvidencePackage(IReadOnlyList<WebhookChunk> chunks)
    {
        // Sort defensively by score rather than trusting upstream order. Ties keep their
        // relative input order (OrderByDescending is stable) — fine since retrieval already
        // ranks reasonably; nothing here needs to be smarter than that.
        var ranked = chunks
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk.Text))
            .Select(chunk => chunk.ToEvidence())
            .OrderByDescending(item => item.Score)
            .ToArray();

        if (ranked.Length == 0)
        {
            return EvidencePackage.Empty;
        }

        var budget = _generation.EvidenceTokenBudget;
        var selected = new List<Evidence>(MaxEvidenceItems);
        var usedTokens = 0;

        foreach (var item in ranked)
        {
            if (selected.Count == MaxEvidenceItems)
            {
                break;
            }

            var estimated = EvidencePromptBuilder.EstimateTokens(item.Content);

            // Fallback: the single highest-ranked chunk alone exceeds the whole budget.
            // Truncate it to the budget rather than sending no evidence — an oversized best
            // chunk still grounds the answer better than empty. Citations are built from this
            // same (truncated) content downstream, so we never cite text the LLM didn't see.
            if (selected.Count == 0 && estimated > budget)
            {
                var truncated = item with { Content = TruncateToTokens(item.Content, budget) };
                selected.Add(truncated);
                usedTokens = EvidencePromptBuilder.EstimateTokens(truncated.Content);
                break;
            }

            if (usedTokens + estimated > budget)
            {
                continue;
            }

            selected.Add(item);
            usedTokens += estimated;
        }

        if (selected.Count != ranked.Length)
        {
            _logger.LogInformation(
                "Evidence selected {AfterCount}/{BeforeCount} chunks within limits " +
                "(<= {MaxItems} items, <= {Budget} tokens; using {UsedTokens}).",
                selected.Count, ranked.Length, MaxEvidenceItems, budget, usedTokens);
        }

        return new EvidencePackage(selected, usedTokens);
    }

    // EstimateTokens ~= chars / 4, so budget * 4 chars caps the estimate at the budget.
    // ponytail: cuts mid-word; a budget guard doesn't need sentence-boundary truncation.
    private static string TruncateToTokens(string text, int tokenBudget)
    {
        var maxChars = tokenBudget * 4;
        return text.Length <= maxChars ? text : text[..maxChars];
    }
}
