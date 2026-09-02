using System.Runtime.CompilerServices;
using System.Text;
using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Application.Generation;
using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application;

/// <summary>
/// Grounded generation use case: normalize -> fail-closed gate -> build payload -> LLM.
/// This service answers only from the chunks it receives and never fabricates context on its own.
/// </summary>
public sealed class GroundedAnswerService : IChatService
{
    // Cut generation the moment the model drifts into text the formatter would strip anyway:
    // a stray Qwen think tag, a Fuentes/Sources section (the UI shows citations separately, per
    // the system prompt), or an echo of the [SOURCE ...] prompt structure. llama-server removes
    // the matched sequence from the output, so this only ever saves wasted tokens.
    private static readonly IReadOnlyList<string> DefaultStopSequences =
    [
        "</think>",
        "\nFuentes",
        "\nSources",
        "\n[SOURCE ",
    ];

    private readonly IQueryNormalizer _normalizer;
    private readonly ILlmProvider _llm;

    public GroundedAnswerService(IQueryNormalizer normalizer, ILlmProvider llm)
    {
        _normalizer = normalizer;
        _llm = llm;
    }

    public async Task<ChatResponse> AnswerAsync(UserQuestion question, EvidencePackage evidence, CancellationToken cancellationToken, string? modelId = null)
    {
        if (evidence.IsEmpty)
        {
            return ChatResponse.Abstention();
        }

        var normalized = _normalizer.Normalize(question);
        var messages = EvidencePromptBuilder.Build(normalized, evidence);
        var response = await _llm.GenerateAsync(
            new LlmRequest(messages) { StopSequences = DefaultStopSequences, ModelId = modelId }, cancellationToken);
        var formattedAnswer = GeneratedAnswerFormatter.Format(response.Content);

        return BuildResponse(formattedAnswer, evidence);
    }

    public async IAsyncEnumerable<ChatAnswerChunk> AnswerStreamingAsync(
        UserQuestion question,
        EvidencePackage evidence,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        string? modelId = null)
    {
        if (evidence.IsEmpty)
        {
            // Fail-closed: deterministic abstention, LLM never invoked (same as AnswerAsync).
            yield return ChatAnswerChunk.Completed(ChatResponse.Abstention());
            yield break;
        }

        var normalized = _normalizer.Normalize(question);
        var messages = EvidencePromptBuilder.Build(normalized, evidence);

        var raw = new StringBuilder();
        await foreach (var chunk in _llm.GenerateStreamingAsync(
            new LlmRequest(messages) { StopSequences = DefaultStopSequences, ModelId = modelId }, cancellationToken))
        {
            raw.Append(chunk.Delta);
            yield return ChatAnswerChunk.Token(chunk.Delta);
        }

        // Format once over the full text (same formatter as the non-streaming path) and build
        // citations from exactly the evidence sent to the model.
        var formattedAnswer = GeneratedAnswerFormatter.Format(raw.ToString());
        yield return ChatAnswerChunk.Completed(BuildResponse(formattedAnswer, evidence));
    }

    // A refusal/abstention answer grounds on nothing, so drop its citations — otherwise the UI shows
    // a "Fuentes" block that falsely implies the documents backed the (non-)answer.
    private static ChatResponse BuildResponse(string answer, EvidencePackage evidence)
    {
        if (RefusalDetector.IsRefusal(answer))
        {
            return new ChatResponse(answer, [], Abstained: true);
        }

        var citations = evidence.Items.Select(e => e.Citation).Distinct().ToArray();
        return new ChatResponse(answer, citations, Abstained: false);
    }
}
