using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Chatbot.Sst.Application.Abstractions;

namespace Chatbot.Sst.Infrastructure.Streaming;

/// <summary>
/// In-process <see cref="IChatEventStream"/> for single-node deployments where Redis isn't
/// configured. Publisher (background generation) and subscriber (the SSE endpoint) live in the
/// same process, so a per-request in-memory buffer gives the same replay-then-follow semantics as
/// Redis Streams without an external dependency. Terminated streams are evicted after a short
/// grace window so a page reload can still replay the just-finished answer.
/// </summary>
public sealed class InMemoryChatEventStream : IChatEventStream
{
    private static readonly TimeSpan EvictAfterTerminal = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, RequestStream> _streams = new();

    public Task PublishAsync(string requestId, ChatStreamEvent evt, CancellationToken cancellationToken)
    {
        var stream = _streams.GetOrAdd(requestId, static _ => new RequestStream());
        stream.Publish(evt);
        if (evt.IsTerminal)
        {
            // Keep the buffer briefly for reconnects, then drop it so the process doesn't accumulate
            // one buffer per request forever.
            _ = EvictLaterAsync(requestId);
        }
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<ChatStreamEvent> SubscribeAsync(string requestId, CancellationToken cancellationToken)
    {
        var stream = _streams.GetOrAdd(requestId, static _ => new RequestStream());
        return stream.ReadAsync(cancellationToken);
    }

    private async Task EvictLaterAsync(string requestId)
    {
        try
        {
            await Task.Delay(EvictAfterTerminal);
        }
        finally
        {
            _streams.TryRemove(requestId, out _);
        }
    }

    private sealed class RequestStream
    {
        private readonly object _gate = new();
        private readonly List<ChatStreamEvent> _events = new();
        private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Publish(ChatStreamEvent evt)
        {
            lock (_gate)
            {
                _events.Add(evt);
                // Swap the signal atomically under the lock, then wake the old waiters. A subscriber
                // captures the signal task under the same lock, so no wakeup can be lost.
                var previous = _signal;
                _signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                previous.TrySetResult();
            }
        }

        public async IAsyncEnumerable<ChatStreamEvent> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var index = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                List<ChatStreamEvent>? pending = null;
                Task wait;
                lock (_gate)
                {
                    if (index < _events.Count)
                    {
                        pending = _events.GetRange(index, _events.Count - index);
                        index = _events.Count;
                    }
                    wait = _signal.Task;
                }

                if (pending is not null)
                {
                    foreach (var evt in pending)
                    {
                        yield return evt;
                        if (evt.IsTerminal)
                        {
                            yield break;
                        }
                    }
                    continue;
                }

                await wait.WaitAsync(cancellationToken);
            }
        }
    }
}
