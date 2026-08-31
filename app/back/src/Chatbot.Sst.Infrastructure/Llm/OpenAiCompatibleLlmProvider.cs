using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
            StopSequences = request.StopSequences?.ToList(),
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

    public async IAsyncEnumerable<LlmStreamChunk> GenerateStreamingAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var payload = new ChatCompletionRequest
        {
            Model = _options.Model,
            Temperature = request.Temperature ?? _options.Temperature,
            MaxTokens = request.MaxOutputTokens ?? _options.MaxOutputTokens,
            StopSequences = request.StopSequences?.ToList(),
            Stream = true,
            Messages = request.Messages
                .Select(m => new ChatMessage { Role = ToWireRole(m.Role), Content = m.Content })
                .ToList()
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        using var response = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // llama-server emits OpenAI-style SSE: lines "data: {json}\n\n", ending with "data: [DONE]".
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                yield break;
            }

            string? delta = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                    && choices[0].TryGetProperty("delta", out var d)
                    && d.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                {
                    delta = c.GetString();
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping malformed SSE chunk from local LLM.");
                continue;
            }

            if (!string.IsNullOrEmpty(delta))
            {
                yield return new LlmStreamChunk(delta);
            }
        }
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
        [JsonPropertyName("stream")] public bool Stream { get; init; }

        // Reuse the KV cache across requests so the static system prompt (~150 tokens) is not
        // re-prefilled every call. llama-server already has the cache enabled; sending the flag
        // explicitly makes the reuse deterministic.
        [JsonPropertyName("cache_prompt")] public bool CachePrompt { get; init; } = true;

        // Stop generation as soon as the model emits a degenerate think tag or trailing prompt
        // echo; llama-server drops the stop text from the output. Omitted from the wire when null.
        [JsonPropertyName("stop")] public List<string>? StopSequences { get; init; }

        // Greedy decoding (temperature 0) on the small IQ4_XS quant is prone to degenerate
        // repetition loops. A mild penalty curbs them without changing factual output.
        [JsonPropertyName("repeat_penalty")] public double RepeatPenalty { get; init; } = 1.1;

        // Qwen3 emits a <think>...</think> block unless thinking is disabled via the chat
        // template. Without this the answer is polluted with "</think>" and wastes prefill/gen
        // budget. Mirrors the enable_thinking=false the llm/model.json contract requires.
        [JsonPropertyName("chat_template_kwargs")]
        public ChatTemplateKwargs ChatTemplateKwargs { get; } = new();
    }

    private sealed class ChatTemplateKwargs
    {
        [JsonPropertyName("enable_thinking")] public bool EnableThinking { get; init; }
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
