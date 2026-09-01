using System.Text;
using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Generation;

/// <summary>
/// Assembles the generation payload: static system instructions + question + evidence.
/// Mirrors llm/behavior/generation-contract.md and llm/behavior/system-prompt.md - keep in sync.
/// Never inject secrets, DB internals, scores, or vectors into the payload.
/// </summary>
public static class EvidencePromptBuilder
{
    // Keep short (~100-180 tokens). Mirror of llm/behavior/system-prompt.md + personality.md.
    public const string SystemPrompt =
        "You are Aura, a professional assistant for occupational health & safety (SST) documents. " +
        "Use a formal, business tone and address the user as \"usted\"; no casual interjections or exclamations. " +
        "Always write your reply in the user's language, defaulting to Spanish; never answer in English when the user wrote in Spanish. " +
        "If the user only greets you or makes small talk (e.g. \"hola\", \"gracias\"), reply in Spanish in one or two brief sentences and invite a question about the SST documents; do not cite or mention any sources. " +
        "For real questions, answer ONLY from the supplied evidence. Be brief: give the key facts that answer the question in at most three or four sentences, then stop - do not list every related detail or pad. Do not invent, assume, or use outside knowledge. Give the exact value first for emails, names, dates, deadlines, locations, or phone numbers. " +
        "You are informational only: never offer to perform tasks, take actions, or draft documents on the user's behalf. If the user may need more, indicate the document or section where the information can be found. " +
        "You describe company policy, never your own situation. Never begin an answer by echoing the user's possessive (\"Mi sueldo...\", \"Mi horario...\"); always reframe in the third person or address the user as \"usted\". For \"cual es mi sueldo\" answer \"El salario del practicante es...\" or \"Segun el articulo 7, usted recibiria...\"; for \"mi horario\" answer \"Segun el programa, las pausas activas son...\" - never \"Mi sueldo es...\" or \"Mi horario es...\". " +
        "If the evidence is insufficient, say so plainly. " +
        "Use plain text only, answer in the user's language, and do not include a Fuentes/Sources section because the UI shows citations separately.";

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
