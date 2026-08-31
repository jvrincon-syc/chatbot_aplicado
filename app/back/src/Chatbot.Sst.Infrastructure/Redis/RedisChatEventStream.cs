using System.Runtime.CompilerServices;
using Chatbot.Sst.Application.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Chatbot.Sst.Infrastructure.Redis;

/// <summary>
/// <see cref="IChatEventStream"/> over Redis Streams. One stream per request:
/// <c>applied:chat:request:{id}:events</c>, capped + TTL'd so it never becomes an archive.
/// </summary>
public sealed class RedisChatEventStream : IChatEventStream
{
    private const string TypeField = "type";
    private const string DataField = "data";
    private const string TerminalField = "terminal";

    private readonly IConnectionMultiplexer _mux;
    private readonly RedisOptions _options;

    public RedisChatEventStream(IConnectionMultiplexer mux, IOptions<RedisOptions> options)
    {
        _mux = mux;
        _options = options.Value;
    }

    private static string Key(string requestId) => $"applied:chat:request:{requestId}:events";

    public async Task PublishAsync(string requestId, ChatStreamEvent evt, CancellationToken cancellationToken)
    {
        var db = _mux.GetDatabase();
        var key = Key(requestId);
        await db.StreamAddAsync(
            key,
            [
                new NameValueEntry(TypeField, evt.EventType),
                new NameValueEntry(DataField, evt.DataJson),
                new NameValueEntry(TerminalField, evt.IsTerminal ? 1 : 0),
            ],
            maxLength: _options.EventStreamMaxLength,
            useApproximateMaxLength: true);
        // Refreshed on every publish: the 24h clock starts from the last event, not the first.
        await db.KeyExpireAsync(key, TimeSpan.FromHours(_options.EventTtlHours));
    }

    public async IAsyncEnumerable<ChatStreamEvent> SubscribeAsync(
        string requestId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var db = _mux.GetDatabase();
        var key = Key(requestId);
        var lastId = "0-0";

        while (!cancellationToken.IsCancellationRequested)
        {
            // Short poll on the high-level StreamReadAsync rather than XREAD BLOCK: StackExchange.Redis
            // multiplexes every command over one connection, and a blocking XREAD stalls that whole
            // connection — it collides with the client command timeout AND serializes every other
            // request's publish/read behind it (proven by the RedisIntegration test's block-on-empty
            // case timing out). 25ms << the ~57ms/token gen cadence, so this adds no perceptible
            // latency. A real XREAD BLOCK would need a dedicated connection per subscriber.
            var entries = await db.StreamReadAsync(key, lastId, count: 64);
            if (entries.Length == 0)
            {
                await Task.Delay(25, cancellationToken);
                continue;
            }

            foreach (var entry in entries)
            {
                lastId = entry.Id!;
                var type = entry[TypeField];
                var data = entry[DataField];
                var terminal = entry[TerminalField] == 1;
                yield return new ChatStreamEvent(type!, data!, terminal);
                if (terminal)
                {
                    yield break;
                }
            }
        }
    }
}
