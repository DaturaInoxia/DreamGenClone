namespace DreamGenClone.Domain.ModelManager;

public enum AppFunction
{
    RolePlayGeneration,
    StoryModeGeneration,
    StorySummarize,
    StoryAnalyze,
    StoryRank,
    ScenarioPreview,
    ScenarioAdapt,
    ScenarioAssistant,
    WritingAssistant,
    RolePlayAssistant,
    ModelAnalysis,
    RolePlaySemanticAnalysis,
    RolePlaySummaryEnhancement,
    RolePlayLocationDetection,
    RolePlayActorSelection,
    RolePlaySteering,
    RolePlayEncounterDetection,
    RolePlaySceneImagePreprocessor,
    RolePlaySceneImage,
    RolePlaySceneImageEditor,
    RolePlaySceneImageEditPromptCompiler,
    RolePlaySceneImageValidator,
    RolePlaySceneBeatAnalyzer,

    /// <summary>
    /// Drafts a character's body card from its character template (B-122). Synchronous and operator-triggered: the
    /// result is shown as a per-field PROPOSAL and is never written straight to the card.
    /// </summary>
    RolePlayCharacterBodyCardDraft
}
