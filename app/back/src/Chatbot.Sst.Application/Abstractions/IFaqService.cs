using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Abstractions;

/// <summary>An approved FAQ answer served from cache/store (Postgres = source of truth, Redis = cache).</summary>
public sealed record FaqAnswer(string Answer, IReadOnlyList<Citation> Citations);

/// <summary>Fast-path lookup before full retrieval. Returns null on miss.</summary>
public interface IFaqService
{
    Task<FaqAnswer?> LookupAsync(NormalizedQuestion query, CancellationToken cancellationToken);
}
