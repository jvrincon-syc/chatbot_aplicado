using System.Globalization;

namespace Chatbot.Sst.Domain;

public enum ChatRequestState
{
    Pending,
    Completed,
    Failed
}

/// <summary>Question received from the frontend and sent to the external chatbot backend.</summary>
public sealed record ChatQuestionSubmission(
    string Question,
    string? ConversationId = null,
    string? MessageId = null,
    int TopK = 6,
    string? ModelId = null);

/// <summary>Published release resolved at runtime before dispatching the question.</summary>
public sealed record PublishedRelease(
    string ProjectId,
    string RagVariantId,
    string RagReleaseId,
    string State,
    DateTimeOffset? PublishedAt = null);

/// <summary>RAG release visible for the configured project + variant, for release listing/visibility.</summary>
public sealed record RagReleaseSummary(
    string RagReleaseId,
    string ProjectId,
    string RagVariantId,
    string State,
    int ReleaseNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValidatedAt = null);

/// <summary>Accepted dispatch returned by the external chatbot backend.</summary>
public sealed record ChatDispatchReceipt(
    string DispatchId,
    string ProjectId,
    string RagVariantId,
    string RagReleaseId,
    string RetrievalProfileId,
    string Question,
    string MessageId,
    int ChunksSent,
    int WebhookStatusCode,
    DateTimeOffset DispatchedAt,
    string? ConversationId = null);

/// <summary>Chunk payload received from the external backend webhook.</summary>
public sealed record WebhookChunk(
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
    private static readonly string[] SourceUrlMetadataKeys =
    [
        "source_url",
        "sourceUrl",
        "document_url",
        "documentUrl",
        "pdf_url",
        "pdfUrl",
        "file_url",
        "fileUrl",
        "url",
        "href"
    ];

    public Evidence ToEvidence()
    {
        var page = PageStart switch
        {
            null => null,
            _ when PageEnd is null || PageEnd == PageStart => PageStart.Value.ToString(CultureInfo.InvariantCulture),
            _ => $"{PageStart.Value.ToString(CultureInfo.InvariantCulture)}-{PageEnd.Value.ToString(CultureInfo.InvariantCulture)}"
        };

        var section = string.IsNullOrWhiteSpace(SectionTitle) ? SectionPath : SectionTitle;
        var title = ResolveCitationTitle();
        var sourceUrl = ResolveSourceUrl();
        return new Evidence(Text, new Citation(DocumentId, title, page, section, sourceUrl), Score);
    }

    private string ResolveCitationTitle()
    {
        if (TryGetMetadataValue("citation_label", out var citationLabel))
        {
            return citationLabel;
        }

        if (TryGetMetadataValue("document_name", out var documentName))
        {
            return documentName;
        }

        return DocumentId;
    }

    private string? ResolveSourceUrl()
    {
        foreach (var key in SourceUrlMetadataKeys)
        {
            if (TryGetMetadataValue(key, out var value) && IsHttpUrl(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool IsHttpUrl(string value)
        => Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private bool TryGetMetadataValue(string key, out string value)
    {
        value = string.Empty;
        if (Metadata is null)
        {
            return false;
        }

        if (Metadata.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw;
            return true;
        }

        foreach (var item in Metadata)
        {
            if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Value))
            {
                value = item.Value;
                return true;
            }
        }

        return false;
    }
}

/// <summary>Webhook delivery that brings the question back with its retrieved chunks.</summary>
public sealed record ChatWebhookDelivery(
    string DispatchId,
    string ProjectId,
    string RagVariantId,
    string RagReleaseId,
    string RetrievalProfileId,
    string Question,
    int TopK,
    IReadOnlyList<WebhookChunk> Chunks,
    DateTimeOffset DispatchedAt,
    string? ConversationId = null,
    string? MessageId = null);

/// <summary>Local request snapshot used by the frontend polling endpoint.</summary>
public sealed record ChatRequestSnapshot(
    string RequestId,
    string Question,
    int TopK,
    ChatRequestState State,
    string? ConversationId = null,
    string? DispatchId = null,
    string? ProjectId = null,
    string? RagVariantId = null,
    string? RagReleaseId = null,
    string? RetrievalProfileId = null,
    int? ChunksSent = null,
    int? WebhookStatusCode = null,
    DateTimeOffset? DispatchedAt = null,
    IReadOnlyList<WebhookChunk>? Chunks = null,
    ChatResponse? Response = null,
    string? ErrorCode = null,
    string? Error = null,
    string? ModelId = null);
