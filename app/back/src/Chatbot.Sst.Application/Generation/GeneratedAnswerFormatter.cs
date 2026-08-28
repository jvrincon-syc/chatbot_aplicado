using System.Text.RegularExpressions;

namespace Chatbot.Sst.Application.Generation;

/// <summary>
/// Normalizes the local LLM's free-text answer into the plain-text shape expected by the UI.
/// The frontend renders citations separately, so inline markdown/source sections are stripped.
/// </summary>
public static class GeneratedAnswerFormatter
{
    private static readonly Regex MultipleBlankLines = new(@"\n{3,}", RegexOptions.Compiled);

    public static string Format(string? rawAnswer)
    {
        if (string.IsNullOrWhiteSpace(rawAnswer))
        {
            return string.Empty;
        }

        var normalized = rawAnswer.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var lines = normalized.Split('\n');
        var keptLines = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            if (IsSourceHeading(line))
            {
                break;
            }

            keptLines.Add(CleanLine(line));
        }

        var cleaned = string.Join("\n", keptLines).Trim();
        return MultipleBlankLines.Replace(cleaned, "\n\n");
    }

    private static bool IsSourceHeading(string line)
    {
        var normalized = StripMarkdownDecorators(line).Trim().TrimEnd(':').Trim();
        return normalized.Equals("fuentes", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("fuente", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("sources", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("source", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanLine(string line)
    {
        var trimmedEnd = line.TrimEnd();

        if (trimmedEnd.StartsWith('#'))
        {
            trimmedEnd = trimmedEnd.TrimStart('#', ' ').TrimStart();
        }

        return StripMarkdownDecorators(trimmedEnd).TrimEnd();
    }

    private static string StripMarkdownDecorators(string text)
        => text
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
}
