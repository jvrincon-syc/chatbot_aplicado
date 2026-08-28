using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Abstractions;

public interface IChatDispatchCoordinator
{
    Task<ChatRequestSnapshot> SubmitAsync(ChatQuestionSubmission submission, CancellationToken cancellationToken);

    ChatRequestSnapshot? Get(string requestId);

    Task<ChatRequestSnapshot?> CompleteAsync(ChatWebhookDelivery delivery, CancellationToken cancellationToken);
}
