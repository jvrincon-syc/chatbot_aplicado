using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Abstractions;

/// <summary>
/// Read-only retrieval against the published RAG product bound to <paramref name="target"/>.
/// The concrete adapter (direct pgvector/FTS vs. platform HTTP API) is deferred until the
/// external RAG consumption contract is confirmed — do not assume a schema here.
/// </summary>
public interface IRagRetriever
{
    Task<RetrievalResult> RetrieveAsync(RagTarget target, NormalizedQuestion query, CancellationToken cancellationToken);
}
