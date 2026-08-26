using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Application.Generation;
using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application;

/// <summary>
/// Grounded generation use case: normalize → (fail-closed gate) → build payload → LLM.
/// Retrieval that produces the evidence is a separate concern (IRagRetriever, not yet wired);
/// this service takes the evidence it is given and never fabricates any.
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
        // Fail-closed: no evidence ⇒ deterministic abstention, no LLM call.
        if (evidence.IsEmpty)
        {
            return ChatResponse.Abstention();
        }

        var normalized = _normalizer.Normalize(question);
        var messages = EvidencePromptBuilder.Build(normalized, evidence);
        var response = await _llm.GenerateAsync(new LlmRequest(messages), cancellationToken);

        var citations = evidence.Items.Select(e => e.Citation).ToArray();
        return new ChatResponse(response.Content, citations, Abstained: false);
    }
}
