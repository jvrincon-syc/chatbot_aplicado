using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
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
        var endpoint = ResolveEndpoint(request.ModelId);
        var payload = new ChatCompletionRequest
        {
            Model = endpoint.Model,
            Temperature = request.Temperature ?? _options.Temperature,
            MaxTokens = request.MaxOutputTokens ?? _options.MaxOutputTokens,
            StopSequences = request.StopSequences?.ToList(),
            ChatTemplateKwargs = endpoint.LlamaCpp ? ThinkingKwargsFor(endpoint.Model) : null,
            CachePrompt = endpoint.LlamaCpp ? true : null,
            RepeatPenalty = endpoint.LlamaCpp ? 1.1 : null,
            Messages = request.Messages
                .Select(m => new ChatMessage { Role = ToWireRole(m.Role), Content = m.Content })
                .ToList()
        };

        using var httpRequest = BuildRequest(endpoint, payload);
        using var response = await _http.SendAsync(httpRequest, cancellationToken);
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
        var endpoint = ResolveEndpoint(request.ModelId);
        var payload = new ChatCompletionRequest
        {
            Model = endpoint.Model,
            Temperature = request.Temperature ?? _options.Temperature,
            MaxTokens = request.MaxOutputTokens ?? _options.MaxOutputTokens,
            StopSequences = request.StopSequences?.ToList(),
            ChatTemplateKwargs = endpoint.LlamaCpp ? ThinkingKwargsFor(endpoint.Model) : null,
            CachePrompt = endpoint.LlamaCpp ? true : null,
            RepeatPenalty = endpoint.LlamaCpp ? 1.1 : null,
            Stream = true,
            Messages = request.Messages
                .Select(m => new ChatMessage { Role = ToWireRole(m.Role), Content = m.Content })
                .ToList()
        };

        using var httpRequest = BuildRequest(endpoint, payload);
        using var response = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        // Explicit UTF-8 (no BOM sniffing) so multi-byte characters that span read buffers are
        // decoded correctly and accents never arrive mangled.
        using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

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

    // enable_thinking is a Qwen3 chat-template kwarg. Emitting it for a non-Qwen3 model (e.g.
    // Qwen2.5) can mis-render the template and produce corrupted/garbled text, so only send it
    // when the configured model is a Qwen3.
    private static ChatTemplateKwargs? ThinkingKwargsFor(string model)
        => model.Contains("qwen3", StringComparison.OrdinalIgnoreCase) ? new ChatTemplateKwargs() : null;

    // LlamaCpp = true when the target is the local/studio llama-server, which accepts its own
    // non-OpenAI extensions (cache_prompt, repeat_penalty, chat_template_kwargs). A remote
    // OpenAI-strict endpoint (Groq) rejects those with 400, so they must be omitted there.
    private readonly record struct ResolvedEndpoint(string Url, string? ApiKey, string Model, bool LlamaCpp);

    // Resolves the request's model selection to a concrete endpoint. A configured profile overrides
    // the default BaseUrl/Model/ApiKey; a profile with empty BaseUrl/Model falls back to the default
    // Llm config, and its key comes from the env var it names (ApiKeyEnv) — Groq is OpenAI-compatible
    // so a Groq profile is just its BaseUrl + model id + GROQ_API_KEY.
    private ResolvedEndpoint ResolveEndpoint(string? modelId)
    {
        var baseUrl = _options.BaseUrl;
        var model = _options.Model;
        var apiKey = _options.ApiKey;
        var llamaCpp = true;  // default endpoint is the local/studio llama-server

        if (!string.IsNullOrWhiteSpace(modelId))
        {
            var profile = _options.Profiles.FirstOrDefault(
                p => string.Equals(p.Id, modelId, StringComparison.OrdinalIgnoreCase));
            if (profile is not null)
            {
                // A profile that overrides BaseUrl points at a different provider (e.g. Groq), which
                // is OpenAI-strict — drop the llama.cpp-only params for it.
                if (!string.IsNullOrWhiteSpace(profile.BaseUrl))
                {
                    baseUrl = profile.BaseUrl;
                    llamaCpp = false;
                }
                if (!string.IsNullOrWhiteSpace(profile.Model)) model = profile.Model;
                var envKey = string.IsNullOrWhiteSpace(profile.ApiKeyEnv)
                    ? null
                    : Environment.GetEnvironmentVariable(profile.ApiKeyEnv);
                if (!string.IsNullOrWhiteSpace(envKey)) apiKey = envKey;
            }
        }

        return new ResolvedEndpoint($"{baseUrl.TrimEnd('/')}/v1/chat/completions", apiKey, model, llamaCpp);
    }

    private static HttpRequestMessage BuildRequest(ResolvedEndpoint endpoint, ChatCompletionRequest payload)
    {
        // Absolute URL overrides the HttpClient BaseAddress; per-request auth overrides its default
        // header — both needed so one client can call the local studio and a remote Groq endpoint.
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
        {
            Content = JsonContent.Create(payload),
        };
        if (!string.IsNullOrWhiteSpace(endpoint.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        }
        return httpRequest;
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

        // Reuse the KV cache across requests so the static system prompt is not re-prefilled every
        // call. llama.cpp-only; omitted (null) for OpenAI-strict endpoints like Groq, which 400 on it.
        [JsonPropertyName("cache_prompt")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? CachePrompt { get; init; }

        // Stop generation as soon as the model emits a degenerate think tag or trailing prompt
        // echo; llama-server drops the stop text from the output. Omitted from the wire when null.
        [JsonPropertyName("stop")] public List<string>? StopSequences { get; init; }

        // Greedy decoding on a small quant is prone to repetition loops; a mild penalty curbs them.
        // llama.cpp-only param — omitted (null) for OpenAI-strict endpoints like Groq (400 otherwise).
        [JsonPropertyName("repeat_penalty")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? RepeatPenalty { get; init; }

        // Qwen3 emits a <think>...</think> block unless thinking is disabled via the chat template.
        // This kwarg is Qwen3-SPECIFIC: sending enable_thinking to a Qwen2.5 (or other) model can
        // mis-render its chat template and corrupt the output, so it is only set for Qwen3 (see
        // ThinkingKwargsFor) and omitted from the wire otherwise.
        [JsonPropertyName("chat_template_kwargs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ChatTemplateKwargs? ChatTemplateKwargs { get; init; }
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
