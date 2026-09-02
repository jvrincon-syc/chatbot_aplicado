using System.ComponentModel.DataAnnotations;

namespace Chatbot.Sst.Infrastructure.Llm;

/// <summary>Typed configuration for the OpenAI-compatible local LLM (llama-server).</summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string Provider { get; init; } = "OpenAiCompatible";

    [Required]
    [Url]
    public string BaseUrl { get; init; } = "http://127.0.0.1:8001";

    [Required]
    public string Model { get; init; } = "qwen3-1.7b";

    /// <summary>
    /// Optional bearer token for a remote OpenAI-compatible endpoint (e.g. a cloud GPU studio).
    /// Null/empty for the local llama-server, which needs no auth. Kept out of appsettings; supply
    /// it via the <c>Llm__ApiKey</c> environment variable so the secret is never committed.
    /// </summary>
    public string? ApiKey { get; init; }

    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; init; } = 60;

    [Range(1, 4096)]
    public int MaxOutputTokens { get; init; } = 200;

    [Range(0.0, 2.0)]
    public double Temperature { get; init; }

    /// <summary>
    /// Selectable model profiles surfaced to the frontend. A profile with an empty BaseUrl falls
    /// back to the top-level Llm config (the default endpoint). Its API key is read from the env var
    /// named by <see cref="LlmProfile.ApiKeyEnv"/> (so keys stay in secrets.env, never appsettings);
    /// empty ApiKeyEnv falls back to the top-level <see cref="ApiKey"/>. Groq is OpenAI-compatible,
    /// so a Groq profile is just BaseUrl=https://api.groq.com/openai/v1 + its model id.
    /// </summary>
    public IReadOnlyList<LlmProfile> Profiles { get; init; } = [];
}

/// <summary>One frontend-selectable LLM (endpoint + model). See <see cref="LlmOptions.Profiles"/>.</summary>
public sealed class LlmProfile
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string? ApiKeyEnv { get; init; }
}
