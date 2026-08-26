using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Chatbot.Sst.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Sst.Infrastructure.Llm;

/// <summary>
/// ILlmProvider over llama-server's OpenAI-compatible /v1/chat/completions endpoint.
/// The only place in the codebase that knows the wire format of the local model server.
/// </summary>
public sealed class OpenAiCompatibleLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<OpenAiCompatibleLlmProvider> _logger;

    public OpenAiCompatibleLlmProvider(
        HttpClient http,
        IOptions<LlmOptions> options,
        ILogger<OpenAiCompatibleLlmProvider> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var payload = new ChatCompletionRequest
        {
            Model = _options.Model,
            Temperature = request.Temperature ?? _options.Temperature,
            MaxTokens = request.MaxOutputTokens ?? _options.MaxOutputTokens,
            Messages = request.Messages
                .Select(m => new ChatMessage { Role = ToWireRole(m.Role), Content = m.Content })
                .ToList()
        };

        using var response = await _http.PostAsJsonAsync("/v1/chat/completions", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken)
                   ?? throw new InvalidOperationException("LLM returned an empty response body.");

        var content = body.Choices.FirstOrDefault()?.Message?.Content ?? string.Empty;
        return new LlmResponse(content)
        {
            PromptTokens = body.Usage?.PromptTokens,
            CompletionTokens = body.Usage?.CompletionTokens
        };
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            // llama-server exposes /health; fall back to the models list if absent.
            using var response = await _http.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Local LLM endpoint is not reachable at {BaseUrl}", _options.BaseUrl);
            return false;
        }
    }

    private static string ToWireRole(LlmRole role) => role switch
    {
        LlmRole.System => "system",
        LlmRole.User => "user",
        LlmRole.Assistant => "assistant",
        _ => "user"
    };

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")] public required string Model { get; init; }
        [JsonPropertyName("messages")] public required List<ChatMessage> Messages { get; init; }
        [JsonPropertyName("temperature")] public double Temperature { get; init; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; init; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public required string Role { get; init; }
        [JsonPropertyName("content")] public required string Content { get; init; }
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")] public List<Choice> Choices { get; init; } = [];
        [JsonPropertyName("usage")] public Usage? Usage { get; init; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; init; }
    }

    private sealed class Usage
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; init; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; init; }
    }
}
