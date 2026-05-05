namespace DreamGenClone.Infrastructure.Configuration;

public sealed class RolePlayDecisionOptions
{
    public const string SectionName = "RolePlayDecision";

    // Controls whether the next continuation suppresses narrative after a decision is applied.
    public bool SuppressNarrativeAfterDecision { get; set; } = false;

    // Controls whether the narrative is suppressed on the turn a phase transition occurs.
    public bool SuppressNarrativeAfterPhaseChange { get; set; } = false;

    // Feature flag for creating decision prompts when the narrative phase changes.
    public bool EnablePhaseChangeDecisionPrompts { get; set; } = false;

    // Feature flag for creating decision prompts when scene location changes.
    public bool EnableSceneLocationDecisionPrompts { get; set; } = false;

    // Feature flag for AI-generated question header text on decision prompts.
    public bool EnableAiDecisionQuestionText { get; set; } = false;

    // Master feature flag — when false, the entire decision prompt system is bypassed:
    // no decision points are created, no LLM rewrite is called, and the UI poll returns nothing.
    public bool EnableDecisionPrompts { get; set; } = false;
}