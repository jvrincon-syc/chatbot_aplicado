using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Abstractions;

/// <summary>
/// Turns a question + its evidence into a grounded answer. Fail-closed: if the evidence is empty,
/// it returns a deterministic abstention WITHOUT invoking the LLM.
/// </summary>
public interface IChatService
{
    Task<ChatResponse> AnswerAsync(UserQuestion question, EvidencePackage evidence, CancellationToken cancellationToken);
}
