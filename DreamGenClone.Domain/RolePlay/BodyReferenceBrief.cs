namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The stance a body reference is rendered in.
///
/// Deliberately a short, closed set: these are the only poses verified to HOLD under OpenPoseXL2 on the local
/// ControlNet stack (B-118 pose work, 2026-09-08/09 — standing / squatting / kneeling with feet on the ground).
/// An unverified stance is not offered, because a body reference whose pose silently drifts is not comparable to
/// the next one — and comparability is the whole point of generating several.
/// </summary>
public enum BodyReferenceStance
{
    /// <summary>Upright, weight even, facing the camera — the canonical body reference.</summary>
    Standing = 1,

    /// <summary>Squatting with feet flat on the ground.</summary>
    Squatting = 2,

    /// <summary>Kneeling with feet on the ground (not sitting on the heels).</summary>
    Kneeling = 3
}

/// <summary>
/// The frozen body-reference brief (B-122) — the SINGLE semantic source a body-reference prompt is compiled from.
///
/// It exists for the same reason the Composition Composer keeps one frozen semantic snapshot: the image must record
/// what was actually asked for. Reading the live card at render time cannot do that — two candidates generated
/// three edits apart become unexplainable, and a comparison between them is meaningless if neither states what it
/// was generated from. <see cref="BodyCardVersion"/> and <see cref="BodyShape"/> therefore travel together.
///
/// The body axes are the SAME <see cref="CharacterBodyAxes"/> the card and the character template share — this is
/// not a second vocabulary. The appearance facts (age, hair, eyes, skin) are carried because both target families
/// require them in a body prompt: Pony's validated rule 12 repeats key attributes early because position shapes
/// composition, and the SDXL anatomy puts subject/appearance first.
///
/// Nothing defaults. A required fact that is missing fails fast in <see cref="Validate"/> rather than becoming a
/// guessed value.
/// </summary>
public sealed class BodyReferenceBrief
{
    /// <summary>Body-card version this brief was frozen from — the audit link back to the operator's decision.</summary>
    public int BodyCardVersion { get; set; }

    public string CharacterTemplateId { get; set; } = string.Empty;

    /// <summary>
    /// Required: the character's gender, read from the character template (<c>"Male"</c>/<c>"Female"</c>).
    ///
    /// It is a stated fact, not an inference. The compiler used to derive the noun from whether a bust measurement
    /// was present, which silently rendered a flat-chested woman as a man — a guess dressed up as a mapping.
    /// <c>"Unknown"</c> is refused for the same reason: without a gender there is no honest count tag or noun, and
    /// a wrong one poisons the reference rather than merely omitting detail.
    /// </summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>
    /// Required: which pipeline slot this view fills. The Pony <c>rating_*</c> tag is derived from it, because the
    /// safe rating of a clothed reference and the explicit rating of an unclothed one are both known — the old path
    /// hardcoded <c>rating_explicit</c> for every asset, so a clothed body reference asked for explicit content.
    /// </summary>
    public SceneImageReferenceBodyState BodyState { get; set; }

    // ── Appearance (the person, not the build) ───────────────────────────────────────────────────────────────

    /// <summary>Required: both target families need an explicit age token, or they fall back to their own prior.</summary>
    public string? Age { get; set; }

    public string? HairStyle { get; set; }
    public string? HairColour { get; set; }
    public string? EyeColour { get; set; }
    public string? SkinTone { get; set; }
    public string? SkinTexture { get; set; }
    public string? Ethnicity { get; set; }

    // ── Body: the invariant line plus the axis picks ────────────────────────────────────────────────────────

    /// <summary>The body card's authored line — the single source of the invariant body facts.</summary>
    public string BodyShape { get; set; } = string.Empty;

    /// <summary>The card's own axis picks, carried across so both families can render them in their own dialect.</summary>
    public CharacterBodyAxes Axes { get; set; } = new();

    public string? BodyHair { get; set; }
    public string? PubicHair { get; set; }
    public string? Tattoos { get; set; }
    public string? ScarsMarks { get; set; }

    // ── Composition ─────────────────────────────────────────────────────────────────────────────────────────

    public BodyReferenceStance Stance { get; set; } = BodyReferenceStance.Standing;

    /// <summary>What the subject wears. Null/blank for an unclothed reference; never assumed either way.</summary>
    public string? Clothing { get; set; }

    // ── Identity conditioning (only for models that support it) ─────────────────────────────────────────────

    /// <summary>True when this render should condition on the character's approved face reference.</summary>
    public bool RequiresIdentity { get; set; }

    /// <summary>The approved face reference asset. Required exactly when <see cref="RequiresIdentity"/> is true.</summary>
    public string? IdentityFaceAssetId { get; set; }

    /// <summary>
    /// The approved pack the face reference belongs to. Recorded alongside the asset so the render can RE-VERIFY both
    /// at render time — a pack superseded between queueing and rendering must fail the render, not silently condition
    /// on a retired reference.
    /// </summary>
    public string? IdentityPackId { get; set; }

    /// <summary>
    /// Fails fast, naming what is missing. Deliberately does not fall back to any default: a body reference
    /// generated without an age, or with identity silently dropped, would be indistinguishable from a correct one
    /// in the candidate list and would poison exactly the comparison the operator is making.
    /// </summary>
    public void Validate()
    {
        var missing = new List<string>(4);

        if (string.IsNullOrWhiteSpace(CharacterTemplateId))
        {
            missing.Add("the character template id");
        }

        if (string.IsNullOrWhiteSpace(BodyShape))
        {
            missing.Add("the body shape line (the body card must be complete before a reference is generated)");
        }

        if (string.IsNullOrWhiteSpace(Age))
        {
            missing.Add("the age");
        }

        if (!IsStatedGender(Gender))
        {
            missing.Add(
                $"the gender (read '{Gender}'; the character template must state Male or Female)");
        }

        if (!Enum.IsDefined(BodyState))
        {
            throw new InvalidOperationException(
                $"The body reference brief names body state '{(int)BodyState}', which is not a body state. Use "
                + "Clothed or Unclothed — the Pony rating tag is derived from it.");
        }

        // Clothing and state must agree, both ways. A clothed reference with no outfit leaves the model to invent
        // one, and an unclothed reference carrying an outfit contradicts itself. Neither is normalised away.
        if (BodyState == SceneImageReferenceBodyState.Clothed && string.IsNullOrWhiteSpace(Clothing))
        {
            throw new InvalidOperationException(
                "The body reference brief is for a clothed view but names no clothing, so the model would invent an "
                + "outfit. Give the clothed reference an outfit (the character's DefaultClothing is the usual source).");
        }

        if (BodyState == SceneImageReferenceBodyState.Unclothed && !string.IsNullOrWhiteSpace(Clothing))
        {
            throw new InvalidOperationException(
                $"The body reference brief is for an unclothed view but carries clothing ('{Clothing}'), which "
                + "contradicts the view. Clear it — the unclothed prompt states the absence of clothing positively.");
        }

        if (!Enum.IsDefined(Stance))
        {
            throw new InvalidOperationException(
                $"Unsupported body reference stance '{(int)Stance}'. Supported stances are the OpenPoseXL2-"
                + "verified set: Standing, Squatting, Kneeling.");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The body reference brief for character '{CharacterTemplateId}' is incomplete, so no prompt can be "
                + $"compiled. Provide: {string.Join("; ", missing)}.");
        }

        // Identity is two facts that must agree. Requesting it without a reference, or holding a reference that was
        // not requested, are both contradictions — neither is silently normalised (repo no-fallback rule).
        if (RequiresIdentity && string.IsNullOrWhiteSpace(IdentityFaceAssetId))
        {
            throw new InvalidOperationException(
                "The body reference brief requests identity conditioning but names no approved face reference, so "
                + "there is nothing to condition on. Approve a face reference for this character, or turn identity "
                + "conditioning off for this render.");
        }

        // The same two-way contract for the pack: a face asset without its pack could not be verified at render time.
        if (RequiresIdentity && string.IsNullOrWhiteSpace(IdentityPackId))
        {
            throw new InvalidOperationException(
                "The body reference brief requests identity conditioning but names no approved pack, so the reference "
                + "could not be re-verified before the render. Record the pack the face asset belongs to.");
        }

        if (!RequiresIdentity && !string.IsNullOrWhiteSpace(IdentityFaceAssetId))
        {
            throw new InvalidOperationException(
                $"The body reference brief names face reference '{IdentityFaceAssetId}' but does not request identity "
                + "conditioning, so the reference would be ignored. Set RequiresIdentity or clear the reference.");
        }

        if (!RequiresIdentity && !string.IsNullOrWhiteSpace(IdentityPackId))
        {
            throw new InvalidOperationException(
                $"The body reference brief names identity pack '{IdentityPackId}' but does not request identity "
                + "conditioning, so the pack would be ignored. Set RequiresIdentity or clear the pack.");
        }
    }

    /// <summary>
    /// True only for a gender the compiler can render honestly. "Unknown" (the template's own unset value) and a
    /// blank are both refused: neither yields a count tag or a noun, and inventing one would be a guess.
    /// </summary>
    public static bool IsStatedGender(string? gender)
        => gender?.Trim() switch
        {
            null => false,
            "" => false,
            var value when string.Equals(value, "Male", StringComparison.OrdinalIgnoreCase) => true,
            var value when string.Equals(value, "Female", StringComparison.OrdinalIgnoreCase) => true,
            var value when string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase) => false,
            _ => false
        };
}
