namespace DreamGenClone.Domain.RolePlay;

public sealed class SceneMomentEnrichment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CatalogueId { get; set; } = string.Empty;
    public string BeatId { get; set; } = string.Empty;
    public string BeatProductionPlanId { get; set; } = string.Empty;
    public int BeatProductionPlanVersion { get; set; }
    public string MomentSetId { get; set; } = string.Empty;
    public int MomentSetVersion { get; set; }
    public string MomentId { get; set; } = string.Empty;
    public int Revision { get; set; }
    public SceneBeatCatalogueStatus Status { get; set; } = SceneBeatCatalogueStatus.Pending;
    public string? CurrentAttemptId { get; set; }
    public int SchemaVersion { get; set; }
    public string PromptContractVersion { get; set; } = string.Empty;
    public string MomentSnapshotJson { get; set; } = string.Empty;
    public string TurnEvidenceSnapshotJson { get; set; } = string.Empty;
    public string FrozenStateContractJson { get; set; } = string.Empty;
    public string InstantaneousSoundEventsJson { get; set; } = string.Empty;
    public string VideoKeyStateJson { get; set; } = string.Empty;
    public string? ModelIdentifier { get; set; }
    public string? ProviderName { get; set; }
    public string ExecutionSettingsJson { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed record SceneMomentFrozenCharacter(
    string ProfileKey,
    string CharacterId,
    string Name,
    string Involvement,
    string PhysicalLocation,
    string Position,
    string ActionOrObservation,
    string Sightline,
    IReadOnlyList<string> VisibleCharacterNames,
    string Clothing);

public sealed record SceneMomentFrozenStateContract(
    string VisualDescription,
    IReadOnlyList<SceneMomentFrozenCharacter> Characters,
    string Location,
    string TimeOfDay,
    string Lighting,
    string Environment,
    string Mood,
    IReadOnlyList<string> Objects,
    string ContinuityState);

public sealed record SceneMomentInstantaneousSoundEvent(
    string CueKey,
    string EventKey,
    string Description);

public sealed record SceneMomentVideoKeyState(
    IReadOnlyList<string> Roles,
    bool StateChangeAllowed);

public sealed record SceneMomentEnrichmentData(
    string FrozenStateContractJson,
    string InstantaneousSoundEventsJson,
    string VideoKeyStateJson);
