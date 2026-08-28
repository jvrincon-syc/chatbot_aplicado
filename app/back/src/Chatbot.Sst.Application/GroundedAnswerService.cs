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
    private readonly IQueryNormalizer _normalizer;
    private readonly ILlmProvider _llm;

    public GroundedAnswerService(IQueryNormalizer normalizer, ILlmProvider llm)
    {
        _normalizer = normalizer;
        _llm = llm;
    }

    public async Task<ChatResponse> AnswerAsync(UserQuestion question, EvidencePackage evidence, CancellationToken cancellationToken)
    {
        if (evidence.IsEmpty)
        {
            return ChatResponse.Abstention();
        }

        var normalized = _normalizer.Normalize(question);
        var messages = EvidencePromptBuilder.Build(normalized, evidence);
        var response = await _llm.GenerateAsync(new LlmRequest(messages), cancellationToken);
        var formattedAnswer = GeneratedAnswerFormatter.Format(response.Content);

        var citations = evidence.Items.Select(e => e.Citation).Distinct().ToArray();
        return new ChatResponse(formattedAnswer, citations, Abstained: false);
    }
}
