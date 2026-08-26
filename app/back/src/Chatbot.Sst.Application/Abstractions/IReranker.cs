using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Abstractions;

/// <summary>Reorders candidates by relevance to the query before the evidence budget is applied.</summary>
public interface IReranker
{
    Task<IReadOnlyList<RetrievalCandidate>> RerankAsync(
        NormalizedQuestion query,
        IReadOnlyList<RetrievalCandidate> candidates,
        CancellationToken cancellationToken);
}
