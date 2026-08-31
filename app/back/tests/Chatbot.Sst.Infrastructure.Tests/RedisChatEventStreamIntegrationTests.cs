using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Chatbot.Sst.Infrastructure.Tests;

/// <summary>
/// Exercises the REAL <see cref="RedisChatEventStream"/> against a live Redis (not the in-memory
/// fake used by the coordinator unit tests). Covers the XREAD BLOCK read path + ParseXReadResult
/// added in FIX 3: a publish/subscribe roundtrip, and the block-and-wait path where the subscriber
/// is already blocked on an empty stream before the events are published.
///
/// Run explicitly against a reachable Redis:
///   REDIS_PASSWORD=... dotnet test --filter "Category=RedisIntegration"
/// </summary>
[Trait("Category", "RedisIntegration")]
public sealed class RedisChatEventStreamIntegrationTests
{
    private static (RedisChatEventStream Stream, IConnectionMultiplexer Mux) Build()
    {
        var configuration = Environment.GetEnvironmentVariable("REDIS_CONFIGURATION") ?? "localhost:6379";
        var password = Environment.GetEnvironmentVariable("REDIS_PASSWORD");

        var options = ConfigurationOptions.Parse(configuration);
        if (!string.IsNullOrEmpty(password))
        {
            options.Password = password;
        }
        options.AbortOnConnectFail = false;

        var mux = ConnectionMultiplexer.Connect(options);
        var stream = new RedisChatEventStream(
            mux, Options.Create(new RedisOptions { Configuration = configuration, Password = password }));
        return (stream, mux);
    }

    private static string NewRequestId() => "itest_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Publish_then_subscribe_roundtrips_deltas_and_terminal_via_xread()
    {
        var (stream, mux) = Build();
        var requestId = NewRequestId();
        try
        {
            await stream.PublishAsync(requestId,
                new ChatStreamEvent("chat.answer.delta.v1", "{\"delta\":\"Hola\"}", false), CancellationToken.None);
            await stream.PublishAsync(requestId,
                new ChatStreamEvent("chat.answer.delta.v1", "{\"delta\":\" mundo\"}", false), CancellationToken.None);
            await stream.PublishAsync(requestId,
                new ChatStreamEvent("chat.answer.completed.v1", "{\"answer\":\"Hola mundo\"}", true), CancellationToken.None);

            var received = new List<ChatStreamEvent>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await foreach (var evt in stream.SubscribeAsync(requestId, cts.Token))
            {
                received.Add(evt);
                if (evt.IsTerminal)
                {
                    break;
                }
            }

            Assert.Equal(3, received.Count);
            Assert.Equal("chat.answer.delta.v1", received[0].EventType);
            Assert.Contains("Hola", received[0].DataJson);
            Assert.False(received[0].IsTerminal);
            Assert.Equal("chat.answer.completed.v1", received[2].EventType);
            Assert.True(received[2].IsTerminal);
        }
        finally
        {
            await mux.GetDatabase().KeyDeleteAsync($"applied:chat:request:{requestId}:events");
            await mux.CloseAsync();
        }
    }

    [Fact]
    public async Task Subscribe_wakes_on_events_published_after_it_blocks_on_empty_stream()
    {
        // The whole point of XREAD BLOCK: the subscriber blocks on an empty stream and is woken
        // the instant an event is published, with no fixed poll delay.
        var (stream, mux) = Build();
        var requestId = NewRequestId();
        try
        {
            var received = new List<ChatStreamEvent>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var consumer = Task.Run(async () =>
            {
                await foreach (var evt in stream.SubscribeAsync(requestId, cts.Token))
                {
                    received.Add(evt);
                    if (evt.IsTerminal)
                    {
                        break;
                    }
                }
            });

            await Task.Delay(300); // let the consumer enter XREAD BLOCK on the empty stream
            await stream.PublishAsync(requestId,
                new ChatStreamEvent("chat.answer.delta.v1", "{\"delta\":\"x\"}", false), CancellationToken.None);
            await stream.PublishAsync(requestId,
                new ChatStreamEvent("chat.answer.completed.v1", "{\"answer\":\"x\"}", true), CancellationToken.None);

            await consumer;

            Assert.Equal(2, received.Count);
            Assert.False(received[0].IsTerminal);
            Assert.True(received[1].IsTerminal);
        }
        finally
        {
            await mux.GetDatabase().KeyDeleteAsync($"applied:chat:request:{requestId}:events");
            await mux.CloseAsync();
        }
    }
}
