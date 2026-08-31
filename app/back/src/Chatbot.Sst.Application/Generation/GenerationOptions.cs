namespace Chatbot.Sst.Application.Generation;

/// <summary>
/// Generation-time evidence limits. The item cap is a hard invariant (see
/// <see cref="ChatDispatchCoordinator"/>); the token budget is the tuning knob —
/// benchmark 800/1000/1200 without recompiling.
/// </summary>
public sealed class GenerationOptions
{
    public const string SectionName = "Generation";

    /// <summary>Max estimated evidence tokens copied into the LLM prompt (prefill guard).</summary>
    public int EvidenceTokenBudget { get; init; } = 1000;
}
