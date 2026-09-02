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

    /// <summary>
    /// Deterministic relevance gate: if the best retrieved chunk's rerank score is below this, the
    /// request is answered with a fixed "not enough information" reply and the LLM is never called —
    /// stops the small model inventing an answer from loosely-related evidence (e.g. attributing a
    /// role to a name that only appears in a nearby document). Null disables the gate. The coordinator
    /// logs the observed max score for every request so this can be tuned against real traffic.
    /// </summary>
    public double? MinEvidenceScore { get; init; }
}
