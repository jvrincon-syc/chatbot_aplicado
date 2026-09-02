using System.Text.Json.Serialization;
using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Api;

public sealed record StartChatRequest(
    string Question,
    string? ConversationId = null,
    string? MessageId = null,
    int? TopK = null,
    string? ModelId = null)
{
    public ChatQuestionSubmission ToDomain()
        => new(
            Question.Trim(),
            ChatContractHelpers.NormalizeOptional(ConversationId),
            ChatContractHelpers.NormalizeOptional(MessageId),
            TopK ?? 6,
            ChatContractHelpers.NormalizeOptional(ModelId));
}

public sealed record ChatRequestStatusResponse(
    string RequestId,
    string Question,
    string State,
    string? ConversationId = null,
    string? DispatchId = null,
    string? ProjectId = null,
    string? RagVariantId = null,
    string? RagReleaseId = null,
    string? RetrievalProfileId = null,
    int? TopK = null,
    int? ChunksSent = null,
    int? WebhookStatusCode = null,
    DateTimeOffset? DispatchedAt = null,
    IReadOnlyList<ChatRequestChunkResponse>? Chunks = null,
    string? Answer = null,
    IReadOnlyList<Citation>? Citations = null,
    bool? Abstained = null,
    string? ErrorCode = null,
    string? Error = null)
{
    public static ChatRequestStatusResponse From(ChatRequestSnapshot snapshot)
        => new(
            snapshot.RequestId,
            snapshot.Question,
            ToWireState(snapshot.State),
            snapshot.ConversationId,
            snapshot.DispatchId,
            snapshot.ProjectId,
            snapshot.RagVariantId,
            snapshot.RagReleaseId,
            snapshot.RetrievalProfileId,
            snapshot.TopK,
            snapshot.ChunksSent,
            snapshot.WebhookStatusCode,
            snapshot.DispatchedAt,
            snapshot.Chunks?.Select(ChatRequestChunkResponse.From).ToArray(),
            snapshot.Response?.Answer,
            snapshot.Response?.Citations,
            snapshot.Response?.Abstained,
            snapshot.ErrorCode,
            snapshot.Error);

    private static string ToWireState(ChatRequestState state) => state switch
    {
        ChatRequestState.Pending => "pending",
        ChatRequestState.Completed => "completed",
        ChatRequestState.Failed => "failed",
        _ => "pending"
    };
}

public sealed record ChatRequestChunkResponse(
    string NodeId,
    string DocumentId,
    string Text,
    double Score,
    string Source,
    int? PageStart = null,
    int? PageEnd = null,
    string? ParentNodeId = null,
    string? ChildChunkId = null,
    string? SectionTitle = null,
    string? SectionPath = null,
    IReadOnlyDictionary<string, string?>? Metadata = null,
    string? EmbeddingProfileId = null,
    string? CorpusVersion = null)
{
    public static ChatRequestChunkResponse From(WebhookChunk chunk)
        => new(
            chunk.NodeId,
            chunk.DocumentId,
            chunk.Text,
            chunk.Score,
            chunk.Source,
            chunk.PageStart,
            chunk.PageEnd,
            chunk.ParentNodeId,
            chunk.ChildChunkId,
            chunk.SectionTitle,
            chunk.SectionPath,
            chunk.Metadata,
            chunk.EmbeddingProfileId,
            chunk.CorpusVersion);
}

public sealed record ChatWebhookRequest(
    [property: JsonPropertyName("dispatch_id")] string DispatchId,
    [property: JsonPropertyName("project_id")] string ProjectId,
    [property: JsonPropertyName("rag_variant_id")] string RagVariantId,
    [property: JsonPropertyName("rag_release_id")] string RagReleaseId,
    [property: JsonPropertyName("retrieval_profile_id")] string RetrievalProfileId,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("conversation_id")] string? ConversationId,
    [property: JsonPropertyName("message_id")] string? MessageId,
    [property: JsonPropertyName("top_k")] int? TopK,
    [property: JsonPropertyName("chunks")] IReadOnlyList<ChatWebhookChunkRequest>? Chunks,
    [property: JsonPropertyName("dispatched_at")] DateTimeOffset DispatchedAt)
{
    public ChatWebhookDelivery ToDomain()
        => new(
            DispatchId.Trim(),
            ProjectId.Trim(),
            RagVariantId.Trim(),
            RagReleaseId.Trim(),
            RetrievalProfileId.Trim(),
            Question.Trim(),
            TopK ?? 6,
            (Chunks ?? []).Select(chunk => chunk.ToDomain()).ToArray(),
            DispatchedAt,
            ChatContractHelpers.NormalizeOptional(ConversationId),
            ChatContractHelpers.NormalizeOptional(MessageId));
}

public sealed record ChatWebhookChunkRequest(
    [property: JsonPropertyName("node_id")] string NodeId,
    [property: JsonPropertyName("document_id")] string DocumentId,
    [property: JsonPropertyName("parent_node_id")] string? ParentNodeId,
    [property: JsonPropertyName("child_chunk_id")] string? ChildChunkId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("score")] double? Score,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("page_start")] int? PageStart,
    [property: JsonPropertyName("page_end")] int? PageEnd,
    [property: JsonPropertyName("section_title")] string? SectionTitle,
    [property: JsonPropertyName("section_path")] string? SectionPath,
    [property: JsonPropertyName("metadata")] Dictionary<string, string?>? Metadata,
    [property: JsonPropertyName("embedding_profile_id")] string? EmbeddingProfileId,
    [property: JsonPropertyName("corpus_version")] string? CorpusVersion)
{
    public WebhookChunk ToDomain()
        => new(
            NodeId.Trim(),
            DocumentId.Trim(),
            Text,
            Score ?? 0,
            Source ?? "unknown",
            PageStart,
            PageEnd,
            ChatContractHelpers.NormalizeOptional(ParentNodeId),
            ChatContractHelpers.NormalizeOptional(ChildChunkId),
            ChatContractHelpers.NormalizeOptional(SectionTitle),
            ChatContractHelpers.NormalizeOptional(SectionPath),
            Metadata,
            ChatContractHelpers.NormalizeOptional(EmbeddingProfileId),
            ChatContractHelpers.NormalizeOptional(CorpusVersion));
}

public sealed record RagReleaseResponse(
    string RagReleaseId,
    string RagVariantId,
    string State,
    int ReleaseNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValidatedAt,
    bool IsActive)
{
    public static RagReleaseResponse From(RagReleaseSummary release)
        => new(
            release.RagReleaseId,
            release.RagVariantId,
            release.State,
            release.ReleaseNumber,
            release.CreatedAt,
            release.ValidatedAt,
            string.Equals(release.State, "published", StringComparison.OrdinalIgnoreCase));
}

internal static class ChatContractHelpers
{
    public static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
