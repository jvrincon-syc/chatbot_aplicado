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
}
