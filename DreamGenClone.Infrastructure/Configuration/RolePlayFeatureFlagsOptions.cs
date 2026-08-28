namespace DreamGenClone.Infrastructure.Configuration;

public sealed class RolePlayFeatureFlagsOptions
{
    public const string SectionName = "RolePlayFeatureFlags";

    // Master switch for the synchronous adaptive-state update pass executed after each
    // interaction (RolePlayAdaptiveStateService.UpdateFromInteractionAsync). When false,
    // both theme tracker scoring and per-character stat deltas driven by inline regex
    // markers are skipped; AdaptiveState is left untouched on each turn. Async semantic
    // inference (if enabled) still applies its own evidence via ApplyInferredSemanticEvidenceAsync.
    public bool EnableAdaptiveStateUpdates { get; set; } = true;

    // Master switch for the asynchronous semantic interaction analysis pipeline
    // (SemanticInteractionAnalysisJobHandler). When false, no semantic analysis job
    // is enqueued and any pre-enqueued job exits without calling the LLM or mutating state.
    public bool EnableSemanticInference { get; set; } = true;

    // When true, and the session has no active scenario/theme committed yet (Observing
    // window), the continuation prompt includes a short "candidate menu" listing the
    // session's candidate theme labels. This gives the model awareness of the option
    // space without committing to any one theme. When false the prompt omits the menu
    // and relies solely on persona, scenario summary and recent context.
    public bool IncludeCandidateMenuWhileObserving { get; set; } = true;
}
