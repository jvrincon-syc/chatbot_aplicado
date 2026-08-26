using System.Net;
using System.Text;
using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chatbot.Sst.Infrastructure.Tests;

public class OpenAiCompatibleLlmProviderTests
{
    private static OpenAiCompatibleLlmProvider Build(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8001") };
        var options = Options.Create(new LlmOptions { Model = "qwen3-1.7b" });
        return new OpenAiCompatibleLlmProvider(http, options, NullLogger<OpenAiCompatibleLlmProvider>.Instance);
    }

    [Fact]
    public async Task GenerateAsync_parses_openai_chat_completion()
    {
        const string json = """
        {"choices":[{"message":{"role":"assistant","content":"ok"}}],
         "usage":{"prompt_tokens":12,"completion_tokens":1}}
        """;
        var provider = Build(new StubHandler(HttpStatusCode.OK, json));

        var result = await provider.GenerateAsync(
            new LlmRequest([new LlmMessage(LlmRole.User, "hi")]), CancellationToken.None);

        Assert.Equal("ok", result.Content);
        Assert.Equal(12, result.PromptTokens);
        Assert.Equal(1, result.CompletionTokens);
    }

    [Fact]
    public async Task IsAvailableAsync_false_when_endpoint_unreachable()
    {
        var provider = Build(new ThrowingHandler());
        Assert.False(await provider.IsAvailableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task IsAvailableAsync_true_on_healthy_endpoint()
    {
        var provider = Build(new StubHandler(HttpStatusCode.OK, "{}"));
        Assert.True(await provider.IsAvailableAsync(CancellationToken.None));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("connection refused");
    }
}
