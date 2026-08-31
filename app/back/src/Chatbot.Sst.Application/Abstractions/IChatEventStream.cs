namespace Chatbot.Sst.Application.Abstractions;

/// <summary>
/// One lifecycle event for a chat request, as delivered to the browser over SSE.
/// <see cref="DataJson"/> is the already-serialized SSE <c>data:</c> payload.
/// </summary>
public sealed record ChatStreamEvent(string EventType, string DataJson, bool IsTerminal);

/// <summary>
/// Durable per-request event backbone (Redis Streams). Generation publishes answer deltas and the
/// terminal event; the SSE endpoint subscribes. Durable so a page reload / reconnect can replay
/// the stream instead of losing the in-flight answer.
/// </summary>
public interface IChatEventStream
{
    Task PublishAsync(string requestId, ChatStreamEvent evt, CancellationToken cancellationToken);

    /// <summary>
    /// Replays this request's events from the start, then follows live until a terminal event
    /// (answer.completed / request.failed) or cancellation.
    /// </summary>
    IAsyncEnumerable<ChatStreamEvent> SubscribeAsync(string requestId, CancellationToken cancellationToken);
}
