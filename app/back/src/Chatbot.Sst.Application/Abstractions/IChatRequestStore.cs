using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Abstractions;

public interface IChatRequestStore
{
    ChatRequestSnapshot CreatePending(ChatQuestionSubmission submission);

    ChatRequestSnapshot? Get(string requestId);

    ChatRequestSnapshot? GetByDispatchId(string dispatchId);

    ChatRequestSnapshot? AttachDispatchReceipt(string requestId, ChatDispatchReceipt receipt);

    /// <summary>Records that the webhook chunks arrived, ahead of the (slower) LLM generation step.</summary>
    ChatRequestSnapshot? AttachChunks(string requestId, ChatWebhookDelivery delivery);

    ChatRequestSnapshot? Complete(string requestId, ChatWebhookDelivery delivery, ChatResponse response);

    ChatRequestSnapshot? Fail(string requestId, string errorCode, string error, ChatWebhookDelivery? delivery = null);
}
