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
    string Clothing,
    /// <summary>
    /// The camera holder's recorded pitch when this character is the POV (<c>Up</c>, <c>Level</c>, <c>Down</c>), or
    /// null when the enrichment did not record one. B-166 D11: a participant POV is not always eye-level, and the
    /// projection states a pitch only when the brief records it — it never invents one.
    /// </summary>
    string? ViewpointPitch = null,
    /// <summary>
    /// How far the in-frame subject is from the camera holder when this character is the POV
    /// (<c>Near</c>, <c>Mid</c>, <c>Far</c>), or null when not recorded. Same rule as <see cref="ViewpointPitch"/>.
    /// </summary>
    string? ViewpointSubjectDistance = null);

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
