using System.Text.RegularExpressions;

namespace Chatbot.Sst.Application.Generation;

/// <summary>
/// Deterministic guard for programming/code-generation requests. The local model (~1.7B) can be
/// talked into writing code by re-framing the ask as "SST-related", so a soft prompt rule is not
/// enough: this refuses before the LLM ever runs. Matches only unambiguous code signals — a
/// language name, the word "script", or algorithm/pseudocode — never the bare word "código" (which
/// appears in legitimate legal terms like "código sustantivo del trabajo" or "código de conducta").
/// </summary>
public static partial class CodeRequestDetector
{
    [GeneratedRegex(
        @"(?<![\p{L}])(python|javascript|typescript|c\+\+|c#|c-sharp|powershell|\bbash\b|\bsql\b|\bhtml\b|\bcss\b|golang|\bkotlin\b|\bswift\b|\bruby\b|\bphp\b|\brust\b|scripts?|algoritmo|algorithm|dijkstra|pseudoc[oó]digo|pseudocode)(?![\p{L}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodeSignal();

    public static bool IsCodeRequest(string? question)
        => !string.IsNullOrWhiteSpace(question) && CodeSignal().IsMatch(question);
}
