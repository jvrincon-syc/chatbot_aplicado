using System.Text.RegularExpressions;
using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application;

/// <summary>Trim + collapse internal whitespace. Pure, no I/O. Reused wherever a question is normalized.</summary>
public sealed partial class DefaultQueryNormalizer : IQueryNormalizer
{
    public NormalizedQuestion Normalize(UserQuestion question)
        => new(Whitespace().Replace(question.Text ?? string.Empty, " ").Trim());

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
