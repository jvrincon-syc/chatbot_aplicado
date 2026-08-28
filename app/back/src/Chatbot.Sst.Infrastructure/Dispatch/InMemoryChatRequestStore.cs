using System.Collections.Concurrent;
using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Infrastructure.Dispatch;

/// <summary>Small in-memory store that lets the frontend poll while the webhook completes the answer.</summary>
public sealed class InMemoryChatRequestStore : IChatRequestStore
{
    private readonly ConcurrentDictionary<string, ChatRequestSnapshot> _requests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _dispatchToRequest = new(StringComparer.Ordinal);

    public ChatRequestSnapshot CreatePending(ChatQuestionSubmission submission)
    {
        var requestId = NormalizeOrGenerateMessageId(submission.MessageId);
        var snapshot = new ChatRequestSnapshot(
            RequestId: requestId,
            Question: submission.Question,
            TopK: submission.TopK,
            State: ChatRequestState.Pending,
            ConversationId: NormalizeOptional(submission.ConversationId));

        _requests[requestId] = snapshot;
        return snapshot;
    }

    public ChatRequestSnapshot? Get(string requestId)
        => _requests.TryGetValue(requestId, out var snapshot) ? snapshot : null;

    public ChatRequestSnapshot? GetByDispatchId(string dispatchId)
        => _dispatchToRequest.TryGetValue(dispatchId, out var requestId) ? Get(requestId) : null;

    public ChatRequestSnapshot? AttachDispatchReceipt(string requestId, ChatDispatchReceipt receipt)
    {
        if (!_requests.TryGetValue(requestId, out var current))
        {
            return null;
        }

        var updated = current with
        {
            ConversationId = receipt.ConversationId ?? current.ConversationId,
            DispatchId = receipt.DispatchId,
            ProjectId = receipt.ProjectId,
            RagVariantId = receipt.RagVariantId,
            RagReleaseId = receipt.RagReleaseId,
            RetrievalProfileId = receipt.RetrievalProfileId,
            ChunksSent = receipt.ChunksSent,
            WebhookStatusCode = receipt.WebhookStatusCode,
            DispatchedAt = receipt.DispatchedAt
        };

        _requests[requestId] = updated;
        _dispatchToRequest[receipt.DispatchId] = requestId;
        return updated;
    }

    public ChatRequestSnapshot? AttachChunks(string requestId, ChatWebhookDelivery delivery)
    {
        if (!_requests.TryGetValue(requestId, out var current))
        {
            return null;
        }

        var updated = current with
        {
            ConversationId = delivery.ConversationId ?? current.ConversationId,
            DispatchId = delivery.DispatchId,
            ProjectId = delivery.ProjectId,
            RagVariantId = delivery.RagVariantId,
            RagReleaseId = delivery.RagReleaseId,
            RetrievalProfileId = delivery.RetrievalProfileId,
            ChunksSent = delivery.Chunks.Count,
            WebhookStatusCode = current.WebhookStatusCode ?? 202,
            DispatchedAt = delivery.DispatchedAt,
            Chunks = delivery.Chunks
        };

        _requests[requestId] = updated;
        _dispatchToRequest[delivery.DispatchId] = requestId;
        return updated;
    }

    public ChatRequestSnapshot? Complete(string requestId, ChatWebhookDelivery delivery, ChatResponse response)
    {
        if (!_requests.TryGetValue(requestId, out var current))
        {
            return null;
        }

        var updated = current with
        {
            State = ChatRequestState.Completed,
            ConversationId = delivery.ConversationId ?? current.ConversationId,
            DispatchId = delivery.DispatchId,
            ProjectId = delivery.ProjectId,
            RagVariantId = delivery.RagVariantId,
            RagReleaseId = delivery.RagReleaseId,
            RetrievalProfileId = delivery.RetrievalProfileId,
            ChunksSent = delivery.Chunks.Count,
            WebhookStatusCode = current.WebhookStatusCode ?? 202,
            DispatchedAt = delivery.DispatchedAt,
            Chunks = delivery.Chunks,
            Response = response,
            ErrorCode = null,
            Error = null
        };

        _requests[requestId] = updated;
        _dispatchToRequest[delivery.DispatchId] = requestId;
        return updated;
    }

    public ChatRequestSnapshot? Fail(string requestId, string errorCode, string error, ChatWebhookDelivery? delivery = null)
    {
        if (!_requests.TryGetValue(requestId, out var current))
        {
            return null;
        }

        var updated = current with
        {
            State = ChatRequestState.Failed,
            ConversationId = delivery?.ConversationId ?? current.ConversationId,
            DispatchId = delivery?.DispatchId ?? current.DispatchId,
            ProjectId = delivery?.ProjectId ?? current.ProjectId,
            RagVariantId = delivery?.RagVariantId ?? current.RagVariantId,
            RagReleaseId = delivery?.RagReleaseId ?? current.RagReleaseId,
            RetrievalProfileId = delivery?.RetrievalProfileId ?? current.RetrievalProfileId,
            ChunksSent = delivery?.Chunks.Count ?? current.ChunksSent,
            WebhookStatusCode = delivery is null ? current.WebhookStatusCode : current.WebhookStatusCode ?? 202,
            DispatchedAt = delivery?.DispatchedAt ?? current.DispatchedAt,
            Chunks = delivery?.Chunks ?? current.Chunks,
            ErrorCode = errorCode,
            Error = error
        };

        _requests[requestId] = updated;
        if (!string.IsNullOrWhiteSpace(delivery?.DispatchId))
        {
            _dispatchToRequest[delivery.DispatchId] = requestId;
        }

        return updated;
    }

    private static string NormalizeOrGenerateMessageId(string? messageId)
        => string.IsNullOrWhiteSpace(messageId) ? $"msg_{Guid.NewGuid():N}" : messageId.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
