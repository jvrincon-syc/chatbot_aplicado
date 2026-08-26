namespace Chatbot.Sst.Application.Abstractions;

/// <summary>
/// Port to the local generation model. The application/domain layers depend on this
/// abstraction only — never on llama.cpp / Qwen / OpenAI SDK concepts.
/// </summary>
public interface ILlmProvider
{
    Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken);

    /// <summary>True when the underlying model endpoint is reachable.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}

public enum LlmRole
{
    System,
    User,
    Assistant
}

public sealed record LlmMessage(LlmRole Role, string Content);

/// <summary>
/// A generation request. Keep it small: short system instructions + question + evidence.
/// Never the full corpus, DB internals, vectors, scores, or secrets.
/// </summary>
public sealed record LlmRequest(IReadOnlyList<LlmMessage> Messages)
{
    public int? MaxOutputTokens { get; init; }
    public double? Temperature { get; init; }
}

public sealed record LlmResponse(string Content)
{
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
}
