using System.Net;
using Chatbot.Sst.Application;
using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Domain;
using Chatbot.Sst.Infrastructure.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chatbot.Sst.Infrastructure.Tests;

/// <summary>
/// Covers the async-decoupling fix: SubmitAsync must return the pending snapshot
/// immediately instead of blocking on the external dispatch call, and the background
/// dispatch task must never strand a request in Pending on an unexpected failure.
/// </summary>
public sealed class ChatDispatchCoordinatorTests
{
    [Fact]
    public async Task SubmitAsync_returns_before_the_external_dispatch_call_completes()
    {
        var gate = new TaskCompletionSource();
        var dispatchClient = new GatedDispatchClient(gate.Task);
        var store = new InMemoryChatRequestStore();
        var coordinator = new ChatDispatchCoordinator(
            store, dispatchClient, new NeverCalledChatService(), NullLogger<ChatDispatchCoordinator>.Instance);

        var submitTask = coordinator.SubmitAsync(
            new ChatQuestionSubmission("question", "conv_1", "msg_1", 8), CancellationToken.None);

        var completed = await Task.WhenAny(submitTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(submitTask, completed);

        var snapshot = await submitTask;
        Assert.Equal(ChatRequestState.Pending, snapshot.State);
        Assert.Null(snapshot.DispatchId);

        gate.SetResult();
        await WaitUntilAsync(() => store.Get(snapshot.RequestId)?.DispatchId is not null);
        Assert.Equal("chatq_1", store.Get(snapshot.RequestId)!.DispatchId);
    }

    [Fact]
    public async Task Background_dispatch_failure_fails_the_request_instead_of_stranding_it_pending()
    {
        var dispatchClient = new ThrowingDispatchClient();
        var store = new InMemoryChatRequestStore();
        var coordinator = new ChatDispatchCoordinator(
            store, dispatchClient, new NeverCalledChatService(), NullLogger<ChatDispatchCoordinator>.Instance);

        var snapshot = await coordinator.SubmitAsync(
            new ChatQuestionSubmission("question", "conv_1", "msg_2", 8), CancellationToken.None);

        await WaitUntilAsync(() => store.Get(snapshot.RequestId)?.State == ChatRequestState.Failed);

        var final = store.Get(snapshot.RequestId)!;
        Assert.Equal(ChatRequestState.Failed, final.State);
        Assert.Equal("CHATBOT_DISPATCH_UNEXPECTED_FAILURE", final.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_returns_chunks_immediately_without_waiting_for_llm_generation()
    {
        var gate = new TaskCompletionSource();
        var chat = new GatedChatService(gate.Task);
        var store = new InMemoryChatRequestStore();
        var coordinator = new ChatDispatchCoordinator(
            store, new NeverCalledDispatchClient(), chat, NullLogger<ChatDispatchCoordinator>.Instance);
        var pending = store.CreatePending(new ChatQuestionSubmission("Pregunta SST", "conv-1", "msg-3", 8));
        var delivery = CreateDelivery("msg-3");

        var completeTask = coordinator.CompleteAsync(delivery, CancellationToken.None);

        var completed = await Task.WhenAny(completeTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(completeTask, completed);

        var snapshot = await completeTask;
        Assert.NotNull(snapshot);
        Assert.Equal(ChatRequestState.Pending, snapshot!.State);
        Assert.Equal("dispatch-1", snapshot.DispatchId);
        Assert.Equal(2, snapshot.ChunksSent);
        Assert.Null(snapshot.Response);

        gate.SetResult();
        await WaitUntilAsync(() => store.Get(pending.RequestId)?.State == ChatRequestState.Completed);
        Assert.NotNull(store.Get(pending.RequestId)!.Response);
    }

    [Fact]
    public async Task Background_generation_failure_fails_the_request_instead_of_stranding_it_pending()
    {
        var chat = new ThrowingChatService();
        var store = new InMemoryChatRequestStore();
        var coordinator = new ChatDispatchCoordinator(
            store, new NeverCalledDispatchClient(), chat, NullLogger<ChatDispatchCoordinator>.Instance);
        store.CreatePending(new ChatQuestionSubmission("Pregunta SST", "conv-1", "msg-4", 8));
        var delivery = CreateDelivery("msg-4");

        var snapshot = await coordinator.CompleteAsync(delivery, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal(ChatRequestState.Pending, snapshot!.State);

        await WaitUntilAsync(() => store.Get(snapshot.RequestId)?.State == ChatRequestState.Failed);

        var final = store.Get(snapshot.RequestId)!;
        Assert.Equal(ChatRequestState.Failed, final.State);
        Assert.Equal("CHATBOT_LLM_DELIVERY_FAILED", final.ErrorCode);
    }

    [Fact]
    public async Task Background_generation_failure_is_classified_as_unavailable_on_connection_refused()
    {
        var chat = new ThrowingChatService(new HttpRequestException("Connection refused"));
        var store = new InMemoryChatRequestStore();
        var coordinator = new ChatDispatchCoordinator(
            store, new NeverCalledDispatchClient(), chat, NullLogger<ChatDispatchCoordinator>.Instance);
        store.CreatePending(new ChatQuestionSubmission("Pregunta SST", "conv-1", "msg-5", 8));
        var delivery = CreateDelivery("msg-5");

        var snapshot = await coordinator.CompleteAsync(delivery, CancellationToken.None);
        Assert.NotNull(snapshot);

        await WaitUntilAsync(() => store.Get(snapshot!.RequestId)?.State == ChatRequestState.Failed);

        var final = store.Get(snapshot!.RequestId)!;
        Assert.Equal(ChatRequestState.Failed, final.State);
        Assert.Equal("CHATBOT_LLM_UNAVAILABLE", final.ErrorCode);
    }

    [Fact]
    public async Task Background_generation_failure_is_classified_as_timeout_on_task_canceled()
    {
        var chat = new ThrowingChatService(new TaskCanceledException("The request timed out."));
        var store = new InMemoryChatRequestStore();
        var coordinator = new ChatDispatchCoordinator(
            store, new NeverCalledDispatchClient(), chat, NullLogger<ChatDispatchCoordinator>.Instance);
        store.CreatePending(new ChatQuestionSubmission("Pregunta SST", "conv-1", "msg-6", 8));
        var delivery = CreateDelivery("msg-6");

        var snapshot = await coordinator.CompleteAsync(delivery, CancellationToken.None);
        Assert.NotNull(snapshot);

        await WaitUntilAsync(() => store.Get(snapshot!.RequestId)?.State == ChatRequestState.Failed);

        var final = store.Get(snapshot!.RequestId)!;
        Assert.Equal(ChatRequestState.Failed, final.State);
        Assert.Equal("CHATBOT_LLM_TIMEOUT", final.ErrorCode);
    }

    [Fact]
    public async Task Background_generation_failure_falls_back_to_delivery_failed_on_non_success_status()
    {
        var chat = new ThrowingChatService(new HttpRequestException(
            "Response status code does not indicate success: 500 (Internal Server Error).",
            inner: null,
            statusCode: HttpStatusCode.InternalServerError));
        var store = new InMemoryChatRequestStore();
        var coordinator = new ChatDispatchCoordinator(
            store, new NeverCalledDispatchClient(), chat, NullLogger<ChatDispatchCoordinator>.Instance);
        store.CreatePending(new ChatQuestionSubmission("Pregunta SST", "conv-1", "msg-7", 8));
        var delivery = CreateDelivery("msg-7");

        var snapshot = await coordinator.CompleteAsync(delivery, CancellationToken.None);
        Assert.NotNull(snapshot);

        await WaitUntilAsync(() => store.Get(snapshot!.RequestId)?.State == ChatRequestState.Failed);

        var final = store.Get(snapshot!.RequestId)!;
        Assert.Equal(ChatRequestState.Failed, final.State);
        Assert.Equal("CHATBOT_LLM_DELIVERY_FAILED", final.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_caps_evidence_to_top_5_by_score_and_citations_match_kept_set()
    {
        var chat = new CapturingChatService();
        var store = new InMemoryChatRequestStore();
        var coordinator = new ChatDispatchCoordinator(
            store, new NeverCalledDispatchClient(), chat, NullLogger<ChatDispatchCoordinator>.Instance);
        store.CreatePending(new ChatQuestionSubmission("Pregunta SST", "conv-1", "msg-8", 8));
        var delivery = CreateDelivery("msg-8") with
        {
            Chunks =
            [
                new WebhookChunk("n1", "doc-1", "chunk uno", 0.10, "vector"),
                new WebhookChunk("n2", "doc-2", "chunk dos", 0.95, "vector"),
                new WebhookChunk("n3", "doc-3", "chunk tres", 0.20, "vector"),
                new WebhookChunk("n4", "doc-4", "chunk cuatro", 0.88, "vector"),
                new WebhookChunk("n5", "doc-5", "chunk cinco", 0.30, "vector"),
                new WebhookChunk("n6", "doc-6", "chunk seis", 0.99, "vector"),
                new WebhookChunk("n7", "doc-7", "chunk siete", 0.05, "vector"),
            ]
        };

        var snapshot = await coordinator.CompleteAsync(delivery, CancellationToken.None);
        Assert.NotNull(snapshot);

        await WaitUntilAsync(() => chat.CapturedEvidence is not null);

        var evidence = chat.CapturedEvidence!;
        Assert.Equal(5, evidence.Items.Count);
        var keptDocIds = evidence.Items.Select(i => i.Citation.DocumentId).ToHashSet();
        Assert.Equal(new[] { "doc-6", "doc-2", "doc-4", "doc-5", "doc-3" }.ToHashSet(), keptDocIds);
        Assert.DoesNotContain("doc-1", keptDocIds);
        Assert.DoesNotContain("doc-7", keptDocIds);

        await WaitUntilAsync(() => store.Get(snapshot!.RequestId)?.State == ChatRequestState.Completed);
        var citations = store.Get(snapshot!.RequestId)!.Response!.Citations;
        Assert.Equal(keptDocIds, citations.Select(c => c.DocumentId).ToHashSet());
    }

    [Fact]
    public async Task CompleteAsync_keeps_all_evidence_when_chunk_count_is_at_or_below_the_cap()
    {
        var chat = new CapturingChatService();
        var store = new InMemoryChatRequestStore();
        var coordinator = new ChatDispatchCoordinator(
            store, new NeverCalledDispatchClient(), chat, NullLogger<ChatDispatchCoordinator>.Instance);
        store.CreatePending(new ChatQuestionSubmission("Pregunta SST", "conv-1", "msg-9", 8));
        var delivery = CreateDelivery("msg-9"); // 2 chunks, well under the cap

        await coordinator.CompleteAsync(delivery, CancellationToken.None);
        await WaitUntilAsync(() => chat.CapturedEvidence is not null);

        Assert.Equal(2, chat.CapturedEvidence!.Items.Count);
    }

    [Fact]
    public async Task CompleteAsync_does_not_crash_when_evidence_scores_tie()
    {
        var chat = new CapturingChatService();
        var store = new InMemoryChatRequestStore();
        var coordinator = new ChatDispatchCoordinator(
            store, new NeverCalledDispatchClient(), chat, NullLogger<ChatDispatchCoordinator>.Instance);
        store.CreatePending(new ChatQuestionSubmission("Pregunta SST", "conv-1", "msg-10", 8));
        var delivery = CreateDelivery("msg-10") with
        {
            Chunks = Enumerable.Range(1, 7)
                .Select(i => new WebhookChunk($"n{i}", $"doc-{i}", $"chunk {i}", 0.5, "vector"))
                .ToArray()
        };

        await coordinator.CompleteAsync(delivery, CancellationToken.None);
        await WaitUntilAsync(() => chat.CapturedEvidence is not null);

        Assert.Equal(5, chat.CapturedEvidence!.Items.Count);
    }

    private sealed class CapturingChatService : IChatService
    {
        public EvidencePackage? CapturedEvidence { get; private set; }

        public Task<ChatResponse> AnswerAsync(UserQuestion question, EvidencePackage evidence, CancellationToken cancellationToken)
        {
            CapturedEvidence = evidence;
            var citations = evidence.Items.Select(e => e.Citation).ToArray();
            return Task.FromResult(new ChatResponse("Respuesta final", citations, false));
        }
    }

    private static ChatWebhookDelivery CreateDelivery(string messageId)
        => new(
            "dispatch-1",
            "proj_sst-general",
            "ragv_local-bge",
            "ragr_123",
            "retrieval-profile-1",
            "Pregunta SST",
            8,
            [
                new WebhookChunk("node-1", "doc-1", "Texto del primer chunk", 0.91, "vector", 3, 3, SectionTitle: "Incidentes"),
                new WebhookChunk("node-2", "doc-2", "Texto del segundo chunk", 0.84, "hybrid", 7, 8, SectionPath: "Manual > SST")
            ],
            DateTimeOffset.Parse("2026-08-27T12:00:00Z"),
            "conv-1",
            messageId);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("Condition was not met within the timeout.");
    }

    private sealed class GatedDispatchClient(Task gate) : IChatbotDispatchClient
    {
        public async Task<ChatDispatchReceipt> DispatchAsync(ChatQuestionSubmission submission, CancellationToken cancellationToken)
        {
            await gate;
            return new ChatDispatchReceipt(
                "chatq_1", "proj", "ragv", "ragr", "profile", submission.Question,
                submission.MessageId ?? string.Empty, 0, 202, DateTimeOffset.UtcNow, submission.ConversationId);
        }

        public Task<IReadOnlyList<RagReleaseSummary>> ListRagReleasesAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("Should not be called in these tests.");
    }

    private sealed class ThrowingDispatchClient : IChatbotDispatchClient
    {
        public Task<ChatDispatchReceipt> DispatchAsync(ChatQuestionSubmission submission, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom: simulated unexpected dispatch failure.");

        public Task<IReadOnlyList<RagReleaseSummary>> ListRagReleasesAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("Should not be called in these tests.");
    }

    private sealed class NeverCalledChatService : IChatService
    {
        public Task<ChatResponse> AnswerAsync(UserQuestion question, EvidencePackage evidence, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Should not be called in these tests.");
    }

    private sealed class NeverCalledDispatchClient : IChatbotDispatchClient
    {
        public Task<ChatDispatchReceipt> DispatchAsync(ChatQuestionSubmission submission, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Should not be called in these tests.");

        public Task<IReadOnlyList<RagReleaseSummary>> ListRagReleasesAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("Should not be called in these tests.");
    }

    private sealed class GatedChatService(Task gate) : IChatService
    {
        public async Task<ChatResponse> AnswerAsync(UserQuestion question, EvidencePackage evidence, CancellationToken cancellationToken)
        {
            await gate;
            return new ChatResponse("Respuesta final", [new Citation("doc-1", "doc-1", "3", "Incidentes")], false);
        }
    }

    private sealed class ThrowingChatService(Exception? toThrow = null) : IChatService
    {
        private readonly Exception _toThrow = toThrow ?? new InvalidOperationException("boom: simulated unexpected generation failure.");

        public Task<ChatResponse> AnswerAsync(UserQuestion question, EvidencePackage evidence, CancellationToken cancellationToken)
            => throw _toThrow;
    }
}
