using System.Globalization;
using System.Text;

namespace Chatbot.Sst.Application.Generation;

/// <summary>
/// Detects a refusal/abstention/"no information" reply from the local model. Such answers ground on
/// nothing, so pairing them with a "Fuentes" block wrongly implies the cited documents backed the
/// (non-)answer. Matching is accent- and case-insensitive because the model's diacritics are
/// inconsistent, so we normalize both sides before a substring check.
/// </summary>
public static class RefusalDetector
{
    private static readonly string[] Phrases =
    [
        "no tengo informacion",
        "no se proporciona informacion",
        "no encontre informacion",
        "no cuenta con informacion",
        "no dispongo de",
        "no hay informacion",
        "no esta disponible en los documentos",
        "no puedo ayudarte con",
        "solo puedo ayudarte con",
        "no es un tema",
        "no genero codigo",
        "no puedo compartir",
        "no se especifica",
        "no se menciona",
    ];

    public static bool IsRefusal(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        var normalized = StripDiacritics(answer).ToLowerInvariant();
        foreach (var phrase in Phrases)
        {
            if (normalized.Contains(phrase, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string StripDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
