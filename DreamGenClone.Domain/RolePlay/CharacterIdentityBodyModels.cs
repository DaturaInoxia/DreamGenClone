namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Identifies ONE body view request (B-122 Phase 0). A body view is either a canonical view
/// (<see cref="SceneImageReferenceBodyView"/>) or an extended one (a rotation and a position key) — never both,
/// and never neither: the two are different reference slots, and a request that named both could not be promoted
/// with an unambiguous view tag. There is no sweep type: one key is one request.
/// </summary>
public sealed record CharacterIdentityBodyViewKey(
    SceneImageReferenceBodyState State,
    SceneImageReferenceBodyView? View,
    int? RotationDeg,
    string? PositionKey)
{
    /// <summary>A canonical view, the front one being the base every other view is produced from.</summary>
    public static CharacterIdentityBodyViewKey Canonical(
        SceneImageReferenceBodyState state, SceneImageReferenceBodyView view)
        => new(state, view, null, null);

    /// <summary>An extended view: a free rotation plus a position key, carrying no canonical slot.</summary>
    public static CharacterIdentityBodyViewKey Extended(
        SceneImageReferenceBodyState state, int rotationDeg, string positionKey)
        => new(state, null, rotationDeg, positionKey);

    public bool IsCanonical => View is not null;

    /// <summary>
    /// Validates the axis contract. Fails fast naming what is missing or contradictory rather than storing a row
    /// whose state or view nobody can interpret — the pack's BodyView/BodyState contract depends on it.
    /// </summary>
    public void Validate()
    {
        if (!Enum.IsDefined(State))
        {
            throw new InvalidOperationException($"Unsupported body state '{(int)State}'.");
        }

        if (IsCanonical)
        {
            if (!Enum.IsDefined(View!.Value))
            {
                throw new InvalidOperationException($"Unsupported canonical body view '{(int)View.Value}'.");
            }

            if (RotationDeg is not null || !string.IsNullOrWhiteSpace(PositionKey))
            {
                throw new InvalidOperationException(
                    $"A canonical body view ({View}) cannot also carry a rotation or a position key: a view is "
                    + "either canonical or extended.");
            }

            return;
        }

        if (RotationDeg is not { } rotation)
        {
            throw new InvalidOperationException(
                "An extended body view requires its rotation in degrees (and a position key).");
        }

        if (rotation is < -180 or > 180)
        {
            throw new InvalidOperationException(
                $"A body rotation must be between -180 and 180 degrees, but was {rotation}.");
        }

        if (string.IsNullOrWhiteSpace(PositionKey))
        {
            throw new InvalidOperationException(
                "An extended body view requires a position key (for example 'standing' or 'kneeling').");
        }
    }

    /// <summary>
    /// A stable text form of the key, used for persistence lookups and diagnostics. Extended views are keyed by
    /// their rotation and position, so two different extended requests can never share a row.
    /// </summary>
    public string Describe()
    {
        Validate();
        return IsCanonical
            ? $"{State}:{View}"
            : $"{State}:extended:{RotationDeg}:{PositionKey!.Trim().ToLowerInvariant()}";
    }

    /// <summary>The candidate batch every image produced for this request joins.</summary>
    public string BatchIdFor(string buildId)
        => $"body-{buildId.Trim()}-{Describe().Replace(':', '-').ToLowerInvariant()}";
}

/// <summary>
/// A body-invariant check a reviewer records per view. Every one of these is a judgement about whether the view
/// shows the SAME body as the card and the base — none of them is a detector, and deliberately so: no tool in
/// this app can see "the tattoo is in the wrong spot", and inventing one would silently pass drift (B-122
/// B122-011). The checks exist so the judgement is recorded, attributed and enforced instead of remembered.
/// </summary>
public enum CharacterIdentityBodyCheck
{
    /// <summary>Body shape and proportions match the card and the base.</summary>
    BodyShapeConsistency = 1,

    /// <summary>Tattoos and marks are present, with the design and exact placement the card names.</summary>
    TattoosAndMarks = 2,

    /// <summary>Skin tone, body hair and pubic hair match the card.</summary>
    SkinBodyAndPubicHair = 3,

    /// <summary>The reviewer's manual anatomy review — no automated detector exists or is implied.</summary>
    AnatomyReview = 4
}

/// <summary>One check's verdict. <see cref="NotReviewed"/> is the absence of a judgement, not a pass.</summary>
public enum CharacterIdentityBodyCheckVerdict
{
    NotReviewed = 0,
    Pass = 1,
    Fail = 2
}

/// <summary>What a view's checks say, plus who said it and when. One per view (the row it lives on).</summary>
public sealed class CharacterIdentityBodyFindings
{
    public CharacterIdentityBodyCheckVerdict BodyShapeConsistency { get; set; }

    public CharacterIdentityBodyCheckVerdict TattoosAndMarks { get; set; }

    public CharacterIdentityBodyCheckVerdict SkinBodyAndPubicHair { get; set; }

    public CharacterIdentityBodyCheckVerdict AnatomyReview { get; set; }

    public string? Note { get; set; }

    public string? ReviewedBy { get; set; }

    public DateTime? ReviewedUtc { get; set; }

    /// <summary>The verdict recorded for one check.</summary>
    public CharacterIdentityBodyCheckVerdict VerdictFor(CharacterIdentityBodyCheck check) => check switch
    {
        CharacterIdentityBodyCheck.BodyShapeConsistency => BodyShapeConsistency,
        CharacterIdentityBodyCheck.TattoosAndMarks => TattoosAndMarks,
        CharacterIdentityBodyCheck.SkinBodyAndPubicHair => SkinBodyAndPubicHair,
        CharacterIdentityBodyCheck.AnatomyReview => AnatomyReview,
        _ => throw new InvalidOperationException($"Unsupported body check '{check}'.")
    };

    public void Set(CharacterIdentityBodyCheck check, CharacterIdentityBodyCheckVerdict verdict)
    {
        switch (check)
        {
            case CharacterIdentityBodyCheck.BodyShapeConsistency: BodyShapeConsistency = verdict; break;
            case CharacterIdentityBodyCheck.TattoosAndMarks: TattoosAndMarks = verdict; break;
            case CharacterIdentityBodyCheck.SkinBodyAndPubicHair: SkinBodyAndPubicHair = verdict; break;
            case CharacterIdentityBodyCheck.AnatomyReview: AnatomyReview = verdict; break;
            default: throw new InvalidOperationException($"Unsupported body check '{check}'.");
        }
    }

    /// <summary>True when every check has been reviewed and passed.</summary>
    public bool AllPassed => CharacterIdentityBodyChecks.All.All(check => VerdictFor(check) == CharacterIdentityBodyCheckVerdict.Pass);

    /// <summary>The checks that are NOT a recorded pass, in canonical order — what acceptance refuses on.</summary>
    public IReadOnlyList<(CharacterIdentityBodyCheck Check, CharacterIdentityBodyCheckVerdict Verdict)> NotPassed
        => CharacterIdentityBodyChecks.All
            .Select(check => (Check: check, Verdict: VerdictFor(check)))
            .Where(item => item.Verdict != CharacterIdentityBodyCheckVerdict.Pass)
            .ToList();
}

/// <summary>The check set and its labels, defined once so the UI, the gate and the diagnostics agree.</summary>
public static class CharacterIdentityBodyChecks
{
    public static readonly IReadOnlyList<CharacterIdentityBodyCheck> All =
    [
        CharacterIdentityBodyCheck.BodyShapeConsistency,
        CharacterIdentityBodyCheck.TattoosAndMarks,
        CharacterIdentityBodyCheck.SkinBodyAndPubicHair,
        CharacterIdentityBodyCheck.AnatomyReview
    ];

    public static string Label(CharacterIdentityBodyCheck check) => check switch
    {
        CharacterIdentityBodyCheck.BodyShapeConsistency => "body shape and proportions",
        CharacterIdentityBodyCheck.TattoosAndMarks => "tattoos and marks (design and exact placement)",
        CharacterIdentityBodyCheck.SkinBodyAndPubicHair => "skin, body hair and pubic hair",
        CharacterIdentityBodyCheck.AnatomyReview => "anatomy review",
        _ => throw new InvalidOperationException($"Unsupported body check '{check}'.")
    };

    public static CharacterIdentityBodyCheck Require(CharacterIdentityBodyCheck check)
        => All.Contains(check)
            ? check
            : throw new InvalidOperationException($"Unsupported body check '{check}'.");
}

/// <summary>
/// One body view's persisted state: the request it answers, the image it currently points at, the resolved
/// prompt/model that produced it, and its lifecycle status. Reuses the build's status vocabulary
/// (<see cref="CharacterIdentityAngleStatus"/>) because the lifecycle is the same one — not started, pending,
/// complete, accepted, failed, blocked — with no second state machine.
/// </summary>
public sealed class CharacterIdentityBodyView
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string BuildId { get; set; } = string.Empty;

    public SceneImageReferenceBodyState State { get; set; }

    /// <summary>Canonical slot; null for an extended view.</summary>
    public SceneImageReferenceBodyView? View { get; set; }

    /// <summary>Extended view rotation; null for a canonical view.</summary>
    public int? RotationDeg { get; set; }

    /// <summary>Extended view position; null for a canonical view.</summary>
    public string? PositionKey { get; set; }

    public CharacterIdentityAngleStatus Status { get; set; } = CharacterIdentityAngleStatus.NotStarted;

    public string? InputArtifactId { get; set; }

    public string? OutputArtifactId { get; set; }

    public string? ResolvedPromptText { get; set; }

    public string? ResolvedModelId { get; set; }

    public string? FailureReason { get; set; }

    public bool ManualOverrideApplied { get; set; }

    public string? ManualOverrideReason { get; set; }

    public string? ManualOverrideAuthor { get; set; }

    public DateTime? ManualOverrideUtc { get; set; }

    /// <summary>The reviewer's body-invariant findings for this view.</summary>
    public CharacterIdentityBodyFindings Findings { get; set; } = new();

    /// <summary>
    /// B-121's shared reference-quality result for this view's image, recorded through the ONE analyzer. Body
    /// findings <b>extend</b> this; they never replace it, and this never re-implements the eye/landmark path.
    /// </summary>
    public SceneImageReferenceQuality QualityRating { get; set; } = SceneImageReferenceQuality.NotRated;

    public string? QualityNotes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public CharacterIdentityBodyViewKey Key()
        => View is { } canonical
            ? CharacterIdentityBodyViewKey.Canonical(State, canonical)
            : CharacterIdentityBodyViewKey.Extended(State, RotationDeg ?? 0, PositionKey ?? string.Empty);

    public bool IsBase => IsCanonicalBase(State, View);

    /// <summary>The canonical front in either state is the base every other body view is produced from.</summary>
    public static bool IsCanonicalBase(SceneImageReferenceBodyState state, SceneImageReferenceBodyView? view)
        => view == SceneImageReferenceBodyView.Front;
}

/// <summary>
/// One canonical body-reference slot (B-122 Phase 0): a state and a view, its operator-facing label, and whether
/// it is the base of its state. Defined ONCE — the grid, the promotion gate and the readiness diagnostics all read
/// this list, so a slot can never be missing from one of them.
/// </summary>
public sealed record CharacterIdentityBodySlot(
    SceneImageReferenceBodyState State,
    SceneImageReferenceBodyView View,
    string Label,
    bool IsBase);

/// <summary>The canonical body slots — six per state, in the order they are acquired, and the one place they are listed.</summary>
public static class CharacterIdentityBodySlots
{
    public static readonly IReadOnlyList<CharacterIdentityBodySlot> All =
    [
        new(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front, "Clothed Front", true),
        new(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.ThreeQuarterLeft, "Clothed three-quarter left", false),
        new(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.ThreeQuarterRight, "Clothed three-quarter right", false),
        new(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.ProfileLeft, "Clothed profile left", false),
        new(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.ProfileRight, "Clothed profile right", false),
        new(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Back, "Clothed back", false),
        new(SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.Front, "Unclothed Front", true),
        new(SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.ThreeQuarterLeft, "Unclothed three-quarter left", false),
        new(SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.ThreeQuarterRight, "Unclothed three-quarter right", false),
        new(SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.ProfileLeft, "Unclothed profile left", false),
        new(SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.ProfileRight, "Unclothed profile right", false),
        new(SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.Back, "Unclothed back", false)
    ];

    /// <summary>The slots of one state, in canonical order.</summary>
    public static IReadOnlyList<CharacterIdentityBodySlot> For(SceneImageReferenceBodyState state)
        => All.Where(slot => slot.State == state).ToList();

    /// <summary>The slot for a state/view pair, or a refusal naming it.</summary>
    public static CharacterIdentityBodySlot Require(
        SceneImageReferenceBodyState state, SceneImageReferenceBodyView view)
        => All.FirstOrDefault(slot => slot.State == state && slot.View == view)
            ?? throw new InvalidOperationException(
                $"'{state} {view}' is not one of the canonical body slots, so it cannot be acquired or promoted. "
                + "Use an extended view for any other angle.");

    /// <summary>The operator-facing label of a persisted view, extended views included.</summary>
    public static string LabelFor(CharacterIdentityBodyView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.View is { } canonical)
        {
            return Require(view.State, canonical).Label;
        }

        return $"Extended {view.State} view · {view.RotationDeg}° · {view.PositionKey}";
    }
}

/// <summary>
/// The body template keys per request. The prompt is always resolved from the ONE template store; nothing here
/// carries a prompt body.
/// </summary>
public static class CharacterBodyViewPrompts
{
    /// <summary>The acquisition key for a base in the given state.</summary>
    public static string BaseKeyFor(SceneImageReferenceBodyState state) => state switch
    {
        SceneImageReferenceBodyState.Clothed => CharacterBodyWorkflowKeys.ClothedAcquire,
        SceneImageReferenceBodyState.Unclothed => CharacterBodyWorkflowKeys.UnclothedAcquire,
        _ => throw new InvalidOperationException($"Unsupported body state '{state}'.")
    };

    /// <summary>The key for a canonical view, or the extended-view key.</summary>
    public static string KeyFor(CharacterIdentityBodyViewKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        return key.View switch
        {
            SceneImageReferenceBodyView.Front => BaseKeyFor(key.State),
            SceneImageReferenceBodyView.ThreeQuarterLeft => CharacterBodyWorkflowKeys.AngleThreeQuarterLeft,
            SceneImageReferenceBodyView.ThreeQuarterRight => CharacterBodyWorkflowKeys.AngleThreeQuarterRight,
            SceneImageReferenceBodyView.ProfileLeft => CharacterBodyWorkflowKeys.AngleProfileLeft,
            SceneImageReferenceBodyView.ProfileRight => CharacterBodyWorkflowKeys.AngleProfileRight,
            _ => CharacterBodyWorkflowKeys.ExtendedView
        };
    }

    /// <summary>
    /// The key of the CAMERA CLAUSE an angle render appends to the compiled body prompt. Only the four canonical
    /// angles have one — the front is the base, and every other angle is an extended view produced as an edit —
    /// so anything else refuses here rather than resolving a key that does not exist.
    /// </summary>
    public static string RenderKeyFor(CharacterIdentityBodyViewKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        return key.View switch
        {
            SceneImageReferenceBodyView.ThreeQuarterLeft => CharacterBodyWorkflowKeys.RenderThreeQuarterLeft,
            SceneImageReferenceBodyView.ThreeQuarterRight => CharacterBodyWorkflowKeys.RenderThreeQuarterRight,
            SceneImageReferenceBodyView.ProfileLeft => CharacterBodyWorkflowKeys.RenderProfileLeft,
            SceneImageReferenceBodyView.ProfileRight => CharacterBodyWorkflowKeys.RenderProfileRight,
            SceneImageReferenceBodyView.Back => CharacterBodyWorkflowKeys.RenderBack,
            _ => throw new InvalidOperationException(
                $"The {key.Describe()} has no angle-render camera clause: only the four canonical angles are rendered "
                + "from an accepted body, and the front is the base.")
        };
    }
}
