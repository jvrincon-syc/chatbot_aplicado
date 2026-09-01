namespace Chatbot.Sst.Domain;

/// <summary>Source attribution for a piece of grounding context.</summary>
public sealed record Citation(
    string DocumentId,
    string? DocumentTitle = null,
    string? Page = null,
    string? Section = null,
    string? SourceUrl = null);

/// <summary>A chunk selected by the external context backend and supplied to the local LLM.</summary>
public sealed record Evidence(string Content, Citation Citation, double Score);

/// <summary>The bounded set of chunks that reaches the LLM, plus its estimated token cost.</summary>
public sealed record EvidencePackage(IReadOnlyList<Evidence> Items, int EstimatedTokens)
{
    public bool IsEmpty => Items.Count == 0;

    public static readonly EvidencePackage Empty = new([], 0);
}
