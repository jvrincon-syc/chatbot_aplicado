using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Application.Abstractions;

/// <summary>
/// Turns a question + its evidence into a grounded answer. Fail-closed: if the evidence is empty,
/// it returns a deterministic abstention WITHOUT invoking the LLM.
/// </summary>
public interface IChatService
{
    Task<ChatResponse> AnswerAsync(UserQuestion question, EvidencePackage evidence, CancellationToken cancellationToken);

    /// <summary>
    /// Same fail-closed grounded generation as <see cref="AnswerAsync"/>, but streamed: yields a
    /// <see cref="ChatAnswerChunk"/> per token as the answer forms, then exactly one final chunk
    /// carrying the complete <see cref="ChatResponse"/> (formatted answer + citations).
    /// </summary>
    IAsyncEnumerable<ChatAnswerChunk> AnswerStreamingAsync(UserQuestion question, EvidencePackage evidence, CancellationToken cancellationToken);
}

/// <summary>
/// One streamed step of a grounded answer. Either an incremental <see cref="Delta"/> OR the
/// terminal <see cref="Final"/> response (never both). The final chunk closes the stream.
/// </summary>
public sealed record ChatAnswerChunk(string? Delta, ChatResponse? Final)
{
    public bool IsFinal => Final is not null;

    public static ChatAnswerChunk Token(string delta) => new(delta, null);
    public static ChatAnswerChunk Completed(ChatResponse response) => new(null, response);
}
