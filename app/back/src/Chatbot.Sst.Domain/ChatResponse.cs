namespace Chatbot.Sst.Domain;

/// <summary>
/// Final answer returned to the client. When <see cref="Abstained"/> is true the answer is the
/// deterministic abstention message and no LLM was invoked (fail-closed evidence policy).
/// </summary>
public sealed record ChatResponse(
    string Answer,
    IReadOnlyList<Citation> Citations,
    bool Abstained)
{
    public const string AbstentionMessage =
        "No encontré información suficiente en los documentos disponibles para responder esta pregunta con certeza.";

    public static ChatResponse Abstention() => new(AbstentionMessage, [], Abstained: true);
}
