namespace DreamGenClone.Domain.RolePlay;

public enum ProductionWindowPrecision
{
    Exact = 1,
    Estimated = 2,
    Relative = 3
}

public enum ProductionOverlapPolicy
{
    Disallow = 1,
    Allow = 2,
    Duck = 3,
    Interrupt = 4
}

public enum ProductionReviewStatus
{
    Validated = 1,
    ReviewRequired = 2
}

public enum SceneBeatDialogueKind
{
    Dialogue = 1,
    Narration = 2,
    Thought = 3
}

public enum SceneBeatSoundKind
{
    Ambience = 1,
    SoundEffect = 2,
    MusicSection = 3
}

public enum SceneVideoCoverageKind
{
    MomentHold = 1,
    MomentAction = 2,
    MomentTransition = 3,
    BeatExcerpt = 4,
    WholeBeat = 5
}

public enum TypedMediaReferenceRole
{
    CharacterIdentity = 1,
    VoiceIdentity = 2,
    WardrobeContinuity = 3,
    LocationContinuity = 4,
    PropContinuity = 5,
    Pose = 6,
    Style = 7,
    VideoFirstFrame = 8,
    VideoLastFrame = 9,
    VideoInternalKeyframe = 10,
    SourceVideo = 11,
    SourceSpeech = 12,
    MusicConditioning = 13,
    LipSyncVisualSource = 14
}

public sealed record ProductionTimeWindow(
    decimal? StartSeconds,
    decimal? EndSeconds,
    string? StartEventKey,
    string? EndEventKey,
    string DurationIntent,
    ProductionWindowPrecision Precision,
    ProductionOverlapPolicy OverlapPolicy,
    bool IsContinuityLeadIn = false,
    bool IsContinuityTail = false);

public sealed record TypedMediaReference(
    string ReferenceId,
    TypedMediaReferenceRole Role,
    string MediaKind,
    string? SourceRecordId,
    string? AssetId,
    string? SubjectCharacterId,
    ProductionTimeWindow? Window,
    bool Required);

public sealed record VoicePronunciationLexeme(
    string SourceText,
    string Pronunciation,
    string? Alphabet);

public sealed record VoicePerformanceIntent(
    string? SpeakerCharacterId,
    string LanguageCode,
    string? Locale,
    string Emotion,
    string Intensity,
    string Pace,
    string? AccentIntent,
    IReadOnlyList<string> PauseCues,
    string? OverlapOrInterruption,
    IReadOnlyList<VoicePronunciationLexeme> PronunciationLexemes,
    IReadOnlyList<string> NonVerbalVocalEvents);

public sealed record SceneBeatNarrativeEvent(
    string EventKey,
    int Order,
    string Description,
    IReadOnlyList<string> EvidenceInteractionIds,
    ProductionTimeWindow Window);

public sealed record SceneBeatDialogueCue(
    string Id,
    string BeatProductionPlanId,
    int Order,
    SceneBeatDialogueKind Kind,
    string EventKey,
    string ExactSourceText,
    string DisplayText,
    string NormalizedSpokenText,
    string NormalizationMethod,
    string NormalizationVersion,
    string SourceInteractionId,
    int StartOffset,
    int EndOffset,
    string? SpeakerCharacterId,
    IReadOnlyList<string> AddresseeCharacterIds,
    VoicePerformanceIntent PerformanceIntent,
    ProductionTimeWindow Window,
    bool LipSyncRelevant,
    ProductionReviewStatus ReviewStatus,
    string? ReviewReason);

public sealed record SceneBeatSoundCue(
    string Id,
    string BeatProductionPlanId,
    int Order,
    SceneBeatSoundKind Kind,
    string? EventKey,
    string? LocationSource,
    string? SubjectCharacterId,
    string? ObjectReference,
    string Description,
    string IntensityEnvelope,
    bool Diegetic,
    string SpatialIntent,
    ProductionTimeWindow Window,
    bool Loop,
    string? StemIntent,
    string ContinuityGroup,
    ProductionReviewStatus ReviewStatus,
    string? ReviewReason);

public sealed record SceneBeatMusicSection(
    string SectionKey,
    int Order,
    string Mood,
    IReadOnlyList<string> Instrumentation,
    decimal? TempoBpm,
    string? MusicalKey,
    string TransitionIntent,
    bool Instrumental,
    string ContinuityIntent,
    ProductionTimeWindow Window);

public sealed record SceneBeatActionStep(
    int Order,
    string EventKey,
    string SubjectCharacterId,
    string Action,
    string? TargetCharacterId,
    string? TargetObject,
    string ResultingState);

public sealed record SceneBeatContinuityState(
    string Location,
    IReadOnlyDictionary<string, string> CharacterStates,
    IReadOnlyDictionary<string, string> WardrobeStates,
    IReadOnlyDictionary<string, string> ObjectStates,
    string Lighting,
    string StateSummary);

public sealed record SceneVideoCueAudioOwnership(
    string CueId,
    string OwnershipIntent);

public sealed record SceneVideoCoveragePlan(
    string Id,
    string BeatProductionPlanId,
    string CoverageKey,
    SceneVideoCoverageKind CoverageKind,
    ProductionTimeWindow Window,
    IReadOnlyList<string> SourceEventKeys,
    IReadOnlyList<string> RequiredMomentRoles,
    IReadOnlyList<string> PermittedActionPhases,
    string CameraIntent,
    string LensIntent,
    string MotionIntent,
    string PacingIntent,
    IReadOnlyList<TypedMediaReference> References,
    IReadOnlyList<string> DialogueCueIds,
    IReadOnlyList<string> SoundCueIds,
    IReadOnlyList<string> MusicSectionKeys,
    IReadOnlyList<SceneVideoCueAudioOwnership> AudioOwnership,
    bool LipSyncRequired,
    string PerformanceIntent,
    string DurationFitPolicy,
    ProductionReviewStatus ReviewStatus,
    string? ReviewReason);

public sealed class SceneBeatProductionPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CatalogueId { get; set; } = string.Empty;
    public string BeatId { get; set; } = string.Empty;
    public int CatalogueVersion { get; set; }
    public int Version { get; set; }
    public SceneBeatCatalogueStatus Status { get; set; } = SceneBeatCatalogueStatus.Pending;
    public string? CurrentAttemptId { get; set; }
    public int SchemaVersion { get; set; }
    public string PromptContractVersion { get; set; } = string.Empty;
    public string SourceSnapshotJson { get; set; } = string.Empty;
    public string NarrativeArcJson { get; set; } = string.Empty;
    public string TimelineJson { get; set; } = string.Empty;
    public string NarrationCuesJson { get; set; } = string.Empty;
    public string DialogueCuesJson { get; set; } = string.Empty;
    public string AmbiencePlanJson { get; set; } = string.Empty;
    public string SoundEventCuesJson { get; set; } = string.Empty;
    public string MusicPlanJson { get; set; } = string.Empty;
    public string ActionArcJson { get; set; } = string.Empty;
    public string StartContinuityJson { get; set; } = string.Empty;
    public string EndContinuityJson { get; set; } = string.Empty;
    public string TypedReferencesJson { get; set; } = string.Empty;
    public string VideoCoveragePlansJson { get; set; } = string.Empty;
    public string? ModelIdentifier { get; set; }
    public string? ProviderName { get; set; }
    public string ExecutionSettingsJson { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public IReadOnlyList<SceneBeatDialogueCue> DialogueCues { get; set; } = [];
    public IReadOnlyList<SceneBeatSoundCue> SoundCues { get; set; } = [];
    public IReadOnlyList<SceneVideoCoveragePlan> VideoCoveragePlans { get; set; } = [];
}

public sealed record SceneBeatProductionPlanData(
    string NarrativeArcJson,
    string TimelineJson,
    string NarrationCuesJson,
    string DialogueCuesJson,
    string AmbiencePlanJson,
    string SoundEventCuesJson,
    string MusicPlanJson,
    string ActionArcJson,
    string StartContinuityJson,
    string EndContinuityJson,
    string TypedReferencesJson,
    string VideoCoveragePlansJson,
    IReadOnlyList<SceneBeatDialogueCue> DialogueCues,
    IReadOnlyList<SceneBeatSoundCue> SoundCues,
    IReadOnlyList<SceneVideoCoveragePlan> VideoCoveragePlans);