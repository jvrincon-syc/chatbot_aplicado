namespace Chatbot.Sst.Domain;

/// <summary>Which retrieval lane produced a candidate. Used for observability, not authorization.</summary>
public enum RetrievalLane
{
    Faq,
    Fts,
    Vector
}

/// <summary>
/// Source attribution for a piece of evidence. Shape is intentionally generic — the authoritative
/// field set comes from the external RAG consumption contract (still unresolved).
/// </summary>
public sealed record Citation(
    string DocumentId,
    string? DocumentTitle = null,
    string? Page = null,
    string? Section = null);

/// <summary>A raw hit from retrieval, before reranking/evidence selection.</summary>
public sealed record RetrievalCandidate(
    string Content,
    Citation Citation,
    double Score,
    RetrievalLane Lane);

/// <summary>Result of a retrieval pass: candidates plus which lanes actually ran.</summary>
public sealed record RetrievalResult(
    IReadOnlyList<RetrievalCandidate> Candidates,
    IReadOnlyList<RetrievalLane> LanesExecuted)
{
    public static readonly RetrievalResult Empty = new([], []);
}

/// <summary>A high-quality fragment selected to ground the answer.</summary>
public sealed record Evidence(string Content, Citation Citation, double Score);

/// <summary>The bounded set of evidence that reaches the LLM, plus its estimated token cost.</summary>
public sealed record EvidencePackage(IReadOnlyList<Evidence> Items, int EstimatedTokens)
{
    public bool IsEmpty => Items.Count == 0;

    public static readonly EvidencePackage Empty = new([], 0);
}
