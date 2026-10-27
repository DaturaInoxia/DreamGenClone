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

/// <summary>
/// Where a body is in the WORLD, independent of any camera (B-167 v4). <see cref="Anchor"/> names the place feature
/// the body occupies and <see cref="Posture"/> is the stance, so a frame can be projected for any POV without
/// re-reading a prose sentence.
/// </summary>
public sealed record SceneMomentBodyPosition(string Anchor, string Posture);

/// <summary>
/// What a body faces, in world terms (B-167 v4): a cast member, a place feature, a body area or a direction. Never
/// camera-relative and never inferred — a missing value means "not recorded".
/// </summary>
public sealed record SceneMomentFacing(string Toward);

/// <summary>
/// One body-to-body contact as a FACT (B-167 v4): "his hands on her hips" is a contact, not a sentence that has to be
/// pattern-matched back out of prose. <see cref="From"/>/<see cref="To"/> are cast profile keys.
/// </summary>
public sealed record SceneMomentContact(
    string From,
    string Part,
    string To,
    string TargetPart,
    string Manner);

/// <summary>The one irreducible event sentence of the frozen instant (B-167 v4), with the place feature it happens at.</summary>
public sealed record SceneMomentEvent(string Anchor, string Line);

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
    string? ViewpointSubjectDistance = null,
    /// <summary>
    /// B-167 v4: the body's world position (<c>at.anchor</c> + <c>at.posture</c>), or null for a v3 record. Additive:
    /// the v3 <see cref="PhysicalLocation"/> / <see cref="Position"/> prose stays on the record unchanged, and a
    /// consumer that needs exact geometry reads this and states nothing when it is absent.
    /// </summary>
    SceneMomentBodyPosition? At = null,
    /// <summary>
    /// B-167 v4: what the body faces, in world terms, or null for a v3 record. Never inferred from prose.
    /// </summary>
    SceneMomentFacing? Facing = null);

public sealed record SceneMomentFrozenStateContract(
    string VisualDescription,
    IReadOnlyList<SceneMomentFrozenCharacter> Characters,
    string Location,
    string TimeOfDay,
    string Lighting,
    string Environment,
    string Mood,
    IReadOnlyList<string> Objects,
    string ContinuityState,
    /// <summary>
    /// B-167 v4: body-to-body contacts as facts, or null for a v3 record. An empty list is a valid v4 answer (no
    /// contact in the instant); null means the record predates v4 and no contact can be claimed.
    /// </summary>
    IReadOnlyList<SceneMomentContact>? Contacts = null,
    /// <summary>
    /// B-167 v4: the one event sentence of the instant, or null for a v3 record.
    /// </summary>
    SceneMomentEvent? Event = null);

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
