namespace DreamGenClone.Domain.RolePlay;

public enum SceneImageProductionGroupStatus
{
    Draft = 1,
    InProgress = 2,
    Review = 3,
    Approved = 4,
    Archived = 5
}

public enum SceneImageProductionStage
{
    Composition = 1,
    Identity = 2,
    Finish = 3,

    /// <summary>
    /// A preset edit pass (B-133): relight or re-express an existing attempt from a picked preset whose instruction was
    /// assembled deterministically, with no compiler artifact behind it. Its own stage rather than a flag, because the
    /// compiled stage validates a session, attempt and revision that a preset run does not have.
    /// </summary>
    Preset = 4,

    /// <summary>
    /// A multi-angle camera edit pass: orbit the source subject to a picked azimuth/elevation/distance, assembled
    /// deterministically from the editor LoRA's <c>&lt;sks&gt;</c> grammar, with no compiler artifact behind it. Its
    /// own stage for the same reason a preset run is: the compiled stage validates a session, attempt and revision
    /// that a multi-angle run does not have.
    /// </summary>
    MultiAngle = 5
}

public enum SceneImageAttemptDisposition
{
    Active = 1,
    Shortlisted = 2,
    Rejected = 3,
    Archived = 4
}

public enum SceneImageIdentityPolicy
{
    Required = 1,
    SkippedByUser = 2
}

public enum ApprovedSceneFrameDecisionState
{
    Approved = 1,
    Superseded = 2,
    Revoked = 3
}

public enum SceneImageAttemptRetentionMode
{
    Manual = 1,
    Automatic = 2
}

public sealed class SceneImageAttemptRetentionPolicy
{
    public SceneImageAttemptRetentionMode Mode { get; set; }
    public int? RejectedRetentionDays { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; }
    public long Version { get; set; }
}

public sealed record SceneImageBytePurgeReservation(
    string ImageId,
    string FileRelativePath,
    DateTime ReservedUtc);

public sealed class SceneImageProductionGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string CatalogueId { get; set; } = string.Empty;
    public string BeatId { get; set; } = string.Empty;
    public string BeatProductionPlanId { get; set; } = string.Empty;
    public int BeatProductionPlanVersion { get; set; }
    public string MomentSetId { get; set; } = string.Empty;
    public int MomentSetVersion { get; set; }
    public string MomentId { get; set; } = string.Empty;
    public string MomentEnrichmentId { get; set; } = string.Empty;
    public int MomentEnrichmentRevision { get; set; }
    public string Pov { get; set; } = string.Empty;
    public string? CameraIntentSnapshotJson { get; set; }

    /// <summary>
    /// The per-POV location backdrop the operator bound in the Location stage, as <see cref="SceneImageLocationBackdrop"/>
    /// JSON. Null when this POV is text-only (no backdrop chosen). The group is the durable record the Composition
    /// auto-seed re-reads on every composer open; the composer's bindings are rebuilt per visit.
    /// </summary>
    public string? LocationBackdropJson { get; set; }

    /// <summary>
    /// The operator's UNCOMMITTED prompt-panel state — the source they picked and the intent they typed — as
    /// <c>{ source, userIntent }</c> JSON. Null before they have touched the panel.
    /// </summary>
    /// <remarks>
    /// It lives on the group for the same reason the location backdrop does: the composer's controls are rebuilt per
    /// visit, and a draft the operator typed is not a render setting that only exists once they press Generate Prompt.
    /// Without this, text they had not yet compiled was lost on every reload (reported live 2026-10-18: "I had entered
    /// a user intent prompt, but now I can't find it"). Null means the default panel, not "unknown".
    /// </remarks>
    public string? PromptDraftJson { get; set; }

    public SceneImageProductionGroupStatus Status { get; set; } = SceneImageProductionGroupStatus.Draft;
    public SceneImageIdentityPolicy IdentityPolicy { get; set; } = SceneImageIdentityPolicy.Required;
    public string? IdentitySkipReason { get; set; }
    public string? CurrentApprovedDecisionId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// The per-POV location backdrop an operator bound to a production group in the Location stage. Carries the image id
/// AND its checksum, production version and operator label, because a bare id is not enough for render revalidation —
/// the same fields <c>ReferenceApplicationSelection</c> carries for a reference binding.
/// </summary>
public sealed record SceneImageLocationBackdrop(
    string AssetId,
    string ImageId,
    string Sha256,
    int? ProductionVersion,
    string Label)
{
    /// <summary>A versionless backdrop can only be bound when nothing is known about the image's lineage.</summary>
    public static SceneImageLocationBackdrop Create(
        string assetId,
        string imageId,
        string sha256,
        int? productionVersion,
        string label)
    {
        if (string.IsNullOrWhiteSpace(assetId))
            throw new InvalidOperationException("A location backdrop requires the container asset id.");
        if (string.IsNullOrWhiteSpace(imageId))
            throw new InvalidOperationException("A location backdrop requires the image id.");
        if (string.IsNullOrWhiteSpace(sha256))
            throw new InvalidOperationException("A location backdrop requires the image checksum.");
        if (string.IsNullOrWhiteSpace(label))
            throw new InvalidOperationException("A location backdrop requires the operator-entered image name.");

        return new SceneImageLocationBackdrop(assetId.Trim(), imageId.Trim(), sha256.Trim(), productionVersion, label.Trim());
    }
}

/// <summary>Why a location image cannot be bound as a POV's backdrop.</summary>
public enum SceneImageLocationBindBlockerCode
{
    /// <summary>The image has no production approval.</summary>
    NotApproved = 1,

    /// <summary>The image has no operator-entered name, so a reference picker could not tell it from its siblings.</summary>
    Unnamed = 2,

    /// <summary>The image has no checksum, so a render could not revalidate the exact bytes it was bound from.</summary>
    NoChecksum = 3
}

/// <summary>A reason a location image cannot be bound, with the diagnostic that names it.</summary>
public sealed record SceneImageLocationBindBlocker(SceneImageLocationBindBlockerCode Code, string Message);

/// <summary>
/// The ONE rule for whether an image can be bound as a POV's location backdrop.
///
/// <para>
/// It lives in the domain because two surfaces ask it — the bind itself, which refuses, and the picker/selector that
/// OFFERS images, which must not offer one the bind would then reject, or must say in the same words why it is not
/// offered. Two copies of "is this bindable?" is precisely how a form ends up offering an action the store refuses.
/// </para>
/// </summary>
public static class SceneImageLocationBindGate
{
    /// <summary>The blocker this image carries, or null when it can be bound.</summary>
    public static SceneImageLocationBindBlocker? BlockerFor(SceneAssetImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.ProductionApprovalStatus != SceneAssetProductionApprovalStatus.Approved)
        {
            return new SceneImageLocationBindBlocker(
                SceneImageLocationBindBlockerCode.NotApproved,
                $"Location image '{image.Id}' is not approved for production, so it cannot be bound as a backdrop. " +
                "Approve it in Location Studio first.");
        }

        if (string.IsNullOrWhiteSpace(image.DisplayName))
        {
            return new SceneImageLocationBindBlocker(
                SceneImageLocationBindBlockerCode.Unnamed,
                $"Location image '{image.Id}' has no name, so it cannot be bound as a backdrop. Name it first.");
        }

        if (string.IsNullOrWhiteSpace(image.Sha256))
        {
            return new SceneImageLocationBindBlocker(
                SceneImageLocationBindBlockerCode.NoChecksum,
                $"Location image '{image.Id}' has no checksum, so it cannot be bound.");
        }

        return null;
    }
}

/// <summary>Why one attempt cannot become a production group's approved frame.</summary>
public enum SceneImageFrameApprovalBlockerCode
{
    /// <summary>The attempt belongs to another production group.</summary>
    NotInGroup = 1,

    /// <summary>The attempt has not finished rendering.</summary>
    NotComplete = 2,

    /// <summary>The attempt has no stored bytes — never rendered, or its bytes were purged.</summary>
    NoBytes = 3,

    /// <summary>The attempt has no checksum, so the decision could not prove which bytes it approved.</summary>
    NoChecksum = 4
}

/// <summary>A reason an attempt cannot be approved, with the diagnostic that names it.</summary>
public sealed record SceneImageFrameApprovalBlocker(SceneImageFrameApprovalBlockerCode Code, string Message);

/// <summary>
/// The ONE rule for whether an attempt can become a production group's approved frame — the facts that make an
/// approval EXACT, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// It lives in the domain because three surfaces ask it — the approval itself, which refuses; the studio's own
/// approve control, which must not offer an action the service would then reject; and the composition page's, which
/// must reach the same verdict. Before this, each wrote its own version of the rule and they disagreed: the service
/// demanded an Identity-stage attempt while the composer demanded a skipped identity policy, so the same attempt was
/// approvable on one surface and not the other.
/// </para>
///
/// <para>
/// <b>It deliberately says nothing about which stage an attempt came from.</b> The Composition → Identity → Finish
/// ladder used to be an approval precondition, which made approval mean "this attempt reached the stage the app
/// expected" rather than "this is the frame the operator wants". The operator decides that, and an image may be picked
/// as final as they see fit. What the record must still prove is that the decision names bytes that exist and cannot
/// change underneath it, which is what these four checks are.
/// </para>
///
/// <para>
/// <b>Identity-staleness is deliberately not a blocker either.</b> It is a fact the operator has to see — the identity
/// pass no longer matches the current geometry — but it is information, not a decision to make on their behalf.
/// </para>
/// </remarks>
public static class SceneImageFrameApprovalGate
{
    /// <summary>The blocker this attempt carries, or null when it can be approved.</summary>
    /// <param name="group">The production group the attempt is being approved as the frame of.</param>
    /// <param name="image">The attempt.</param>
    public static SceneImageFrameApprovalBlocker? BlockerFor(SceneImageProductionGroup group, SceneImageRecord image)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(image);

        if (!string.Equals(image.ProductionGroupId, group.Id, StringComparison.Ordinal))
        {
            return new SceneImageFrameApprovalBlocker(
                SceneImageFrameApprovalBlockerCode.NotInGroup,
                $"Attempt '{image.Id}' does not belong to production group '{group.Id}', so it cannot be approved as its "
                + "frame.");
        }

        return BlockerFor(image);
    }

    /// <summary>
    /// The blocker an attempt carries on its own — every correctness fact except which group it belongs to.
    ///
    /// <para>
    /// This overload is for a caller that has already read the attempt THROUGH its group (a list of the group's
    /// attempts), so membership is established by the read and asking for the group again would only restate it. Both
    /// overloads evaluate the same facts, which is the point: a surface that projects a group's attempts and the
    /// service that writes the approval cannot disagree about whether a frame can be approved.
    /// </para>
    /// </summary>
    /// <param name="image">The attempt.</param>
    public static SceneImageFrameApprovalBlocker? BlockerFor(SceneImageRecord image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Status != SceneImageStatus.Complete)
        {
            return new SceneImageFrameApprovalBlocker(
                SceneImageFrameApprovalBlockerCode.NotComplete,
                $"Attempt '{image.Id}' is {image.Status}, so there is nothing to approve yet.");
        }

        if (string.IsNullOrWhiteSpace(image.FileRelativePath) || image.BytesPurgedUtc is not null)
        {
            return new SceneImageFrameApprovalBlocker(
                SceneImageFrameApprovalBlockerCode.NoBytes,
                $"Attempt '{image.Id}' has no stored bytes, so approving it would name a frame that cannot be rendered "
                + "or promoted.");
        }

        if (string.IsNullOrWhiteSpace(image.Sha256))
        {
            return new SceneImageFrameApprovalBlocker(
                SceneImageFrameApprovalBlockerCode.NoChecksum,
                $"Attempt '{image.Id}' has no recorded checksum, so the decision could not prove which bytes it "
                + "approved.");
        }

        return null;
    }
}

/// <summary>
/// The moment→location container link: which location container the operator bound for ONE POV of the moment. Keyed on
/// (<see cref="MomentId"/>, <see cref="Pov"/>) — <see cref="MomentId"/> because that id is stable across enrichment
/// revisions (an enrichment id would be lost on every re-enrichment), and <see cref="Pov"/> because Omniscient and each
/// character POV carry their own location binding; one POV's place must never overwrite another's. One CURRENT row per
/// (moment, POV) (upsert). Written only by the operator's bind action, never automatically.
/// </summary>
public sealed class SceneMomentLocationLink
{
    public string MomentId { get; set; } = string.Empty;

    /// <summary>The POV this binding belongs to — the operator-facing POV name (Omniscient, or a character name).</summary>
    public string Pov { get; set; } = string.Empty;

    public string LocationAssetId { get; set; } = string.Empty;

    /// <summary>The resolved scenario location id, or null when the place is ad-hoc (invented by the RP engine).</summary>
    public string? ScenarioLocationId { get; set; }

    /// <summary>Where the link came from. The only writer is the operator's bind action.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>
    /// The named image this POV bound in the Location stage, as <see cref="SceneImageLocationBackdrop"/> JSON, or null
    /// when this POV resolved the container without binding an image (a text-only location).
    ///
    /// <para>
    /// This is the DURABLE half of one binding: the production group is keyed on the Moment enrichment, so a
    /// re-enrichment creates a new group whose backdrop is empty. The (moment, POV) row is what carries the operator's
    /// binding across that, and re-seeding a fresh group's backdrop from it is the only thing that reads this field.
    /// </para>
    /// </summary>
    public string? BackdropJson { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ApprovedSceneFrameDecision
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProductionGroupId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string SceneImageId { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string CatalogueId { get; set; } = string.Empty;
    public string BeatId { get; set; } = string.Empty;
    public string BeatProductionPlanId { get; set; } = string.Empty;
    public int BeatProductionPlanVersion { get; set; }
    public string MomentSetId { get; set; } = string.Empty;
    public int MomentSetVersion { get; set; }
    public string MomentId { get; set; } = string.Empty;
    public string MomentEnrichmentId { get; set; } = string.Empty;
    public int MomentEnrichmentRevision { get; set; }
    public ApprovedSceneFrameDecisionState Decision { get; set; } = ApprovedSceneFrameDecisionState.Approved;
    public string DecidedBy { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime DecisionUtc { get; set; }
}
