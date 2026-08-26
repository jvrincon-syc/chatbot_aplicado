using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Abstractions;

/// <summary>
/// Produces the query embedding for the vector fallback lane. The embedding profile MUST match the
/// target release (model/dimension/metric) — that compatibility comes from the RAG contract, not the
/// model name. Embed the full normalized question, never a keyword reduction.
/// </summary>
public interface IQueryEmbeddingProvider
{
    Task<ReadOnlyMemory<float>> EmbedAsync(NormalizedQuestion query, CancellationToken cancellationToken);
}
