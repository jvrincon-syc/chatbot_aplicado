using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Abstractions;

/// <summary>Normalizes a raw user question (trim, whitespace, casing) before retrieval. Pure, no I/O.</summary>
public interface IQueryNormalizer
{
    NormalizedQuestion Normalize(UserQuestion question);
}
