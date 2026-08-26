namespace Chatbot.Sst.Domain;

/// <summary>
/// Logical identity of the single published RAG product this deployment consumes.
/// Resolved server-side from trusted configuration — never trusted from the browser.
/// </summary>
public sealed record RagTarget(
    string ProjectId,
    string RagVariantId,
    string RagReleaseId);
