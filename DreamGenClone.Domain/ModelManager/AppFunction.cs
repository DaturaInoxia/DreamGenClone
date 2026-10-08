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
    RolePlayCharacterBodyCardDraft,

    /// <summary>
    /// The image model the character Wardrobe tab renders a garment ITEM with. Its own function rather than the
    /// scene-image default because a wardrobe reference is a catalogue image of one garment, so the model that draws
    /// a scene well is not automatically the model that draws a good garment reference.
    /// </summary>
    RolePlayWardrobeItem,

    /// <summary>
    /// The video model the Video Composer renders a clip with (B-152/B-153). Its own function because a video
    /// render is a ~25 to ~100 minute single-GPU job with its own concurrency, lease and polling bounds, and
    /// because a video model can never be substituted for an image one.
    /// </summary>
    RolePlaySceneVideo
}
