using System.Text;
using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Generation;

/// <summary>
/// Assembles the generation payload: static system instructions + question + evidence.
/// Mirrors llm/behavior/generation-contract.md and llm/behavior/system-prompt.md — keep in sync.
/// Never inject secrets, DB internals, scores, or vectors into the payload.
/// </summary>
public static class EvidencePromptBuilder
{
    // Keep short (~100-180 tokens). Mirror of llm/behavior/system-prompt.md.
    public const string SystemPrompt =
        "You are a documentary assistant for occupational health & safety (SST). " +
        "Answer ONLY from the supplied evidence. Do not invent, assume, or use outside knowledge. " +
        "If the evidence is insufficient, do not guess — say so. Cite the sources you used. " +
        "Answer in the user's language, concisely.";

    public static IReadOnlyList<LlmMessage> Build(NormalizedQuestion question, EvidencePackage evidence)
    {
        var sb = new StringBuilder();
        sb.Append("QUESTION:\n").Append(question.Text).Append("\n\nEVIDENCE:\n");

        for (var i = 0; i < evidence.Items.Count; i++)
        {
            var e = evidence.Items[i];
            var c = e.Citation;
            sb.Append("\n[SOURCE ").Append(i + 1).Append("] ");
            sb.Append("Document: ").Append(c.DocumentTitle ?? c.DocumentId);
            if (!string.IsNullOrWhiteSpace(c.Page)) sb.Append(" | Page: ").Append(c.Page);
            if (!string.IsNullOrWhiteSpace(c.Section)) sb.Append(" | Section: ").Append(c.Section);
            sb.Append('\n').Append(e.Content).Append('\n');
        }

        return
        [
            new LlmMessage(LlmRole.System, SystemPrompt),
            new LlmMessage(LlmRole.User, sb.ToString())
        ];
    }

    /// <summary>Rough token estimate (~4 chars/token). Good enough for budgeting, not billing.</summary>
    public static int EstimateTokens(string text) => (text.Length + 3) / 4;
}
