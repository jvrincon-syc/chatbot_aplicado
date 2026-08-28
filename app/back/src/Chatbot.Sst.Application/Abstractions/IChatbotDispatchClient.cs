using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Abstractions;

public interface IChatbotDispatchClient
{
    Task<ChatDispatchReceipt> DispatchAsync(ChatQuestionSubmission submission, CancellationToken cancellationToken);

    Task<IReadOnlyList<RagReleaseSummary>> ListRagReleasesAsync(CancellationToken cancellationToken);
}
