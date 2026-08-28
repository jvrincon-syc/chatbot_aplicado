using Chatbot.Sst.Domain;
using Chatbot.Sst.Infrastructure.Dispatch;

namespace Chatbot.Sst.Infrastructure.Tests;

public class InMemoryChatRequestStoreTests
{
    [Fact]
    public void AttachChunks_stores_webhook_chunks_while_leaving_the_request_pending()
    {
        var store = new InMemoryChatRequestStore();
        var pending = store.CreatePending(new ChatQuestionSubmission("Pregunta SST", "conv-1", "msg-1", 8));
        var delivery = CreateDelivery("msg-1");

        var snapshot = store.AttachChunks(pending.RequestId, delivery);

        Assert.NotNull(snapshot);
        Assert.Equal(ChatRequestState.Pending, snapshot!.State);
        Assert.Equal("dispatch-1", snapshot.DispatchId);
        Assert.Equal(2, snapshot.ChunksSent);
        Assert.NotNull(snapshot.Chunks);
        Assert.Equal(2, snapshot.Chunks!.Count);
        Assert.Null(snapshot.Response);
    }

    [Fact]
    public void Complete_stores_webhook_chunks_for_polling()
    {
        var store = new InMemoryChatRequestStore();
        var pending = store.CreatePending(new ChatQuestionSubmission("Pregunta SST", "conv-1", "msg-1", 8));
        var delivery = CreateDelivery("msg-1");
        var response = new ChatResponse(
            "Respuesta final",
            [new Citation("doc-1", "doc-1", "3", "Incidentes")],
            false);

        var snapshot = store.Complete(pending.RequestId, delivery, response);

        Assert.NotNull(snapshot);
        Assert.Equal(ChatRequestState.Completed, snapshot!.State);
        Assert.Equal(2, snapshot.ChunksSent);
        Assert.NotNull(snapshot.Chunks);
        Assert.Equal(2, snapshot.Chunks!.Count);
        Assert.Equal("doc-1", snapshot.Chunks[0].DocumentId);
        Assert.Equal("doc-2", snapshot.Chunks[1].DocumentId);
    }

    [Fact]
    public void Fail_preserves_webhook_chunks_when_the_llm_step_breaks()
    {
        var store = new InMemoryChatRequestStore();
        var pending = store.CreatePending(new ChatQuestionSubmission("Pregunta SST", "conv-1", "msg-1", 8));
        var delivery = CreateDelivery("msg-1");

        var snapshot = store.Fail(
            pending.RequestId,
            "CHATBOT_LLM_DELIVERY_FAILED",
            "No se pudo generar la respuesta final con el LLM local.",
            delivery);

        Assert.NotNull(snapshot);
        Assert.Equal(ChatRequestState.Failed, snapshot!.State);
        Assert.Equal(2, snapshot.ChunksSent);
        Assert.NotNull(snapshot.Chunks);
        Assert.Equal("dispatch-1", snapshot.DispatchId);
        Assert.Equal("Pregunta SST", snapshot.Question);
    }

    private static ChatWebhookDelivery CreateDelivery(string messageId)
        => new(
            "dispatch-1",
            "proj_sst-general",
            "ragv_local-bge",
            "ragr_123",
            "retrieval-profile-1",
            "Pregunta SST",
            8,
            [
                new WebhookChunk(
                    "node-1",
                    "doc-1",
                    "Texto del primer chunk",
                    0.91,
                    "vector",
                    3,
                    3,
                    SectionTitle: "Incidentes"),
                new WebhookChunk(
                    "node-2",
                    "doc-2",
                    "Texto del segundo chunk",
                    0.84,
                    "hybrid",
                    7,
                    8,
                    SectionPath: "Manual > SST")
            ],
            DateTimeOffset.Parse("2026-08-27T12:00:00Z"),
            "conv-1",
            messageId);
}
