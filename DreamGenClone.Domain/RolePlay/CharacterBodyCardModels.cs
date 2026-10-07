namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The canonical, character-owned body description (B-122 Phase 0). One row per character, and the ONLY
/// mutable source of the invariant body facts: body shape, height/build, skin, body hair, tattoos with
/// their exact placement, scars/marks, body hair and pubic hair.
///
/// Why it exists: an invariant that is described vaguely does not train. The capture list is explicit —
/// "vague 'a few tattoos' will not train consistently" — so generation is gated on the card being
/// COMPLETE. An empty field is not "none", it is an unanswered decision, and
/// <see cref="RequireReadyForGeneration"/> names every one of them rather than guessing.
/// </summary>
public sealed class CharacterBodyCard
{
    public string CharacterTemplateId { get; set; } = string.Empty;

    /// <summary>Overall body shape and proportions ("curvy, full bust, soft waist, wide hips").</summary>
    public string BodyShape { get; set; } = string.Empty;

    /// <summary>Height and build together ("5'8\", medium build").</summary>
    public string HeightBuild { get; set; } = string.Empty;

    /// <summary>Skin tone and texture ("fair smooth skin").</summary>
    public string Skin { get; set; } = string.Empty;

    /// <summary>
    /// Body-WIDE hair pattern: chest, stomach, arms, legs. A <c>[DECIDE]</c> item: an exact description
    /// ("full chest hair", "very little body hair", "treasure trail only") or an explicit "none". The pubic
    /// region is NOT this field — see <see cref="PubicHair"/>.
    /// </summary>
    public string BodyHair { get; set; } = string.Empty;

    /// <summary>
    /// Tattoos by design AND exact placement ("small flower on left forearm, butterfly on right ankle").
    /// A <c>[DECIDE]</c> item: placement is what makes it trainable.
    /// </summary>
    public string Tattoos { get; set; } = string.Empty;

    /// <summary>Scars, marks, moles and similar permanent features; "none" when there are none.</summary>
    public string ScarsMarks { get; set; } = string.Empty;

    /// <summary>
    /// The pubic region's hair and how it is kept. A <c>[DECIDE]</c> item; "none" is an explicit answer.
    /// Separate from <see cref="BodyHair"/> because the two vary independently: a full chest and a shaved pubic
    /// region is one character, not a contradiction.
    /// </summary>
    public string PubicHair { get; set; } = string.Empty;

    /// <summary>Optimistic-concurrency version; starts at 1 and increments on every save.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// The card's own axis picks. The character template is the STARTING POINT and the card is where the tweaks
    /// live, so the card keeps its own copy of the values — but this is not a second vocabulary: every value comes
    /// from <c>PhysicalAttributesCatalog</c>, the same source the template editor and the prompt formatter read.
    ///
    /// Deliberately NOT one of the gated fields: the completeness contract remains the seven fields above. The axes
    /// are an authoring aid that fills <see cref="BodyShape"/>; BodyShape stays the single authored, gate-checked
    /// line that reaches the prompt, and remains hand-editable.
    /// </summary>
    public CharacterBodyAxes Axes { get; set; } = new();

    /// <summary>
    /// The card's FACE descriptors (B-132). What a model reads when it has to build a face from WORDS instead of
    /// copying one from a pack image, which is what lets a character have a face before any reference exists.
    ///
    /// Deliberately NOT one of the gated fields, for the same reason <see cref="Axes"/> is not: the completeness
    /// contract remains the seven fields above, so adding a face never makes an existing card incomplete and never
    /// blocks a render. An unset face descriptor is simply absent from <see cref="ToFaceLine"/>.
    /// </summary>
    public CharacterFaceAttributes Face { get; set; } = new();

    /// <summary>
    /// THE canonical body text (B-132): one authored description of this character's build and face, and the single
    /// text every consumer pastes — the body-reference renders, the LoRA cell's body element, and the scene prompt's
    /// appearance block. Composed from the picks on demand by
    /// <c>BodyReferencePromptCompiler.ComposeBodyText</c>, then hand-editable, and the edit is what travels.
    ///
    /// Why it is STORED rather than re-derived per consumer: three consumers used to derive their own description from
    /// the same card (`ToPromptLine()` for the LoRA cell, the body compiler for the reference renders, the attribute
    /// formatter for the scene prompts) and they disagreed — the LoRA path still carried height and weight months after
    /// those were dropped everywhere else. One stored text cannot disagree with itself.
    ///
    /// It carries NO height or weight, NO camera or stance clause, NO framing/lighting/lens tail and NO clothing:
    /// those belong to a shot, not to a body, and each consumer supplies its own. The unclothed-only pubic-hair detail
    /// is deliberately absent too — it stays on <see cref="PubicHair"/>, its one source, and a consumer appends it when
    /// its own state is unclothed.
    /// </summary>
    public string BodyText { get; set; } = string.Empty;

    /// <summary>
    /// THE canonical FACE text (B-132): the face's own descriptors as prose - hair, eyes, the bone structure and the
    /// detail - and the companion to <see cref="BodyText"/>, which carries no face facts at all.
    ///
    /// Why the two are SEPARATE rather than one description: a cell's prompt has one slot for the subject and its
    /// templates declare no face element, so the two have to be assembled into that one slot by whoever composes it -
    /// and the whole point of the split is that a bound FACE reference image can then drop the face text WITHOUT
    /// dropping the build. A single text made that impossible: the face kept being described in words while the model
    /// was also being shown a face, so the words contradicted the reference.
    ///
    /// Composed from the picks on demand by <c>BodyReferencePromptCompiler.ComposeFaceText</c> and then hand-editable,
    /// on exactly the same terms as <see cref="BodyText"/>. Skin is deliberately NOT here: tone and texture are
    /// body-wide and already travel on the body text, so repeating them would describe one fact twice.
    /// </summary>
    public string FaceText { get; set; } = string.Empty;

    /// <summary>
    /// True when the card carries the canonical body text, which is what the consumers that compose a prompt read.
    /// Kept OUT of <see cref="UnresolvedFields"/> on purpose: the seven gated fields are the card's completeness
    /// contract, and the text is composed FROM them, so folding it into the same gate would report the same gap twice.
    /// </summary>
    public bool HasBodyText => !string.IsNullOrWhiteSpace(BodyText);
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Applies a prefill proposal to <paramref name="field"/> and reports whether it was written. A field that
    /// already has a value is left ALONE: the operator's decision always wins over a prefill (B-122).
    /// </summary>
    public bool TryApplyPrefill(CharacterBodyCardField field, string? proposedValue)
    {
        if (string.IsNullOrWhiteSpace(proposedValue))
        {
            return false;
        }

        var definition = CharacterBodyCardFields.Require(field);
        if (definition.IsAnswered(this))
        {
            return false;
        }

        definition.Set(this, proposedValue.Trim());
        return true;
    }

    /// <summary>The fields still unanswered, in canonical order.</summary>
    public IReadOnlyList<CharacterBodyCardFieldDefinition> UnresolvedFields
        => CharacterBodyCardFields.All.Where(field => !field.IsAnswered(this)).ToList();

    /// <summary>The <c>[DECIDE]</c> items still unanswered — the ones the capture list calls out by name.</summary>
    public IReadOnlyList<CharacterBodyCardFieldDefinition> UnresolvedDecisions
        => UnresolvedFields.Where(field => field.RequiresDecision).ToList();

    public bool IsReady => UnresolvedFields.Count == 0;

    /// <summary>
    /// Fails fast naming every unanswered field (marking the <c>[DECIDE]</c> ones), so a body prompt can
    /// never be assembled from a partially described body.
    /// </summary>
    public void RequireReadyForGeneration()
    {
        if (IsReady)
        {
            return;
        }

        var missing = string.Join(
            "; ",
            UnresolvedFields.Select(field => field.RequiresDecision
                ? $"{field.Label} (a [DECIDE] item: an exact value, or an explicit \"none\")"
                : field.Label));

        throw new InvalidOperationException(
            $"The body card for character '{CharacterTemplateId}' is incomplete, so no body reference can be "
            + $"generated. Decide and record: {missing}.");
    }

    /// <summary>
    /// The canonical card line, in the capture list's order, pasted verbatim into every body prompt.
    /// Refuses to render while any field is unanswered.
    /// </summary>
    public string ToPromptLine()
    {
        RequireReadyForGeneration();
        return string.Join(
            ", ",
            CharacterBodyCardFields.All
                .Select(field => field.Get(this).Trim())
                .Where(RendersInPrompt));
    }

    /// <summary>
    /// True when a value is a RENDERABLE descriptor. A decision record is not a descriptor: "none" (no scars, no body
    /// hair) and "n/a" tell the operator that a decision was made — they are not something to draw. Pasting them into
    /// the positive prompt adds tokens the model renders as text, which is exactly what happened on the operator's
    /// body prompts on 2026-09-22: the card read ScarsMarks="none" / BodyHair="none" and the resolved prompt contained
    /// "… tattoo of a tree on left calve,, none, clean.".
    ///
    /// Absences are therefore OMITTED from the prompt. Nothing here relies on a negative prompt to undo them.
    /// The completeness gate is unchanged: an unanswered field still refuses to render.
    /// </summary>
    private static bool RendersInPrompt(string value) => !IsDecisionOnly(value);

    /// <summary>The values that record a decision rather than describe a body.</summary>
    public static bool IsDecisionOnly(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
        return trimmed.Length == 0
            || trimmed is "none" or "n/a" or "na" or "no" or "unknown" or "not specified" or "not stated"
                or "unspecified" or "not applicable" or "nil";
    }
}

/// <summary>
/// The card's own FACE picks (B-132). Mirrors the character template's face attributes - the template is the
/// starting point, the card is where the tweaks live - using the vocabulary
/// <c>PhysicalAttributesCatalog</c> already defines, so the editor and the prompt read one source of values.
/// Stored on the card rather than as seven more gated columns, because the face is optional detail: a card with no
/// face stated still renders, it just does not describe the face.
/// </summary>
public sealed class CharacterFaceAttributes
{
    public string? FaceShape { get; set; }
    public string? HairStyle { get; set; }
    public string? HairColour { get; set; }
    public string? EyeShape { get; set; }
    public string? EyeColour { get; set; }
    public string? Eyebrows { get; set; }
    public string? NoseShape { get; set; }
    public string? LipsShape { get; set; }
    public string? Jawline { get; set; }

    /// <summary>Gender-conditional in the editor. NOT the body-wide <see cref="CharacterBodyCard.BodyHair"/>.</summary>
    public string? FacialHair { get; set; }

    /// <summary>Jewellery worn on the face (nose, septum, tongue, eyebrow, lip).</summary>
    public string? FacePiercings { get; set; }

    /// <summary>True when no descriptor has been picked, so there is no face line to compose.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(FaceShape)
        && string.IsNullOrWhiteSpace(HairStyle)
        && string.IsNullOrWhiteSpace(HairColour)
        && string.IsNullOrWhiteSpace(EyeShape)
        && string.IsNullOrWhiteSpace(EyeColour)
        && string.IsNullOrWhiteSpace(Eyebrows)
        && string.IsNullOrWhiteSpace(NoseShape)
        && string.IsNullOrWhiteSpace(LipsShape)
        && string.IsNullOrWhiteSpace(Jawline)
        && string.IsNullOrWhiteSpace(FacialHair)
        && string.IsNullOrWhiteSpace(FacePiercings);
}

public enum CharacterBodyCardField
{
    BodyShape = 1,
    HeightBuild = 2,
    Skin = 3,
    BodyHair = 4,
    Tattoos = 5,
    ScarsMarks = 6,
    PubicHair = 7
}

/// <summary>
/// The body card's own axis picks (B-122). The card MIRRORS the character template's body axes — the template is
/// the starting point, the card is where the tweaks live — using the vocabulary the template already defines.
///
/// Stored as JSON rather than as ten columns so a new axis adds a property instead of a migration, and so the picks
/// stay diffable as one document (which is what a review deck compares). <see cref="Compose"/> produces the
/// descriptor for <see cref="CharacterBodyCard.BodyShape"/>.
/// </summary>
public sealed class CharacterBodyAxes
{
    public string? BodyBuild { get; set; }
    public string? Silhouette { get; set; }
    public string? Adiposity { get; set; }
    public string? FatDistribution { get; set; }
    public string? MuscleMass { get; set; }
    public string? MuscleDefinition { get; set; }
    public string? BustSize { get; set; }
    public string? WaistSize { get; set; }
    public string? HipSize { get; set; }
    public string? ButtSize { get; set; }

    /// <summary>
    /// What the chest axis is CALLED on this card's composed shape line: "bust" for a female character, "chest" for a
    /// male one. Stored WITH the picks (the axes are one JSON document) rather than passed at each call site, because
    /// three places compare the stored line against <see cref="Compose"/> — the studio's staleness warning, the brief
    /// factory's, and the compose button — and a label that travelled separately would make a male card look
    /// permanently stale. The default keeps every existing card's wording exactly as it was.
    /// </summary>
    public string ChestLabel { get; set; } = "bust";

    /// <summary>True when no axis has been picked, so there is nothing to compose.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(BodyBuild)
        && string.IsNullOrWhiteSpace(Silhouette)
        && string.IsNullOrWhiteSpace(Adiposity)
        && string.IsNullOrWhiteSpace(FatDistribution)
        && string.IsNullOrWhiteSpace(MuscleMass)
        && string.IsNullOrWhiteSpace(MuscleDefinition)
        && string.IsNullOrWhiteSpace(BustSize)
        && string.IsNullOrWhiteSpace(WaistSize)
        && string.IsNullOrWhiteSpace(HipSize)
        && string.IsNullOrWhiteSpace(ButtSize);

    /// <summary>
    /// The descriptor line for <see cref="CharacterBodyCard.BodyShape"/>: the axes in canonical order, then the
    /// measurements under their short labels ("bust full, waist narrow, hips wide, rear plump") — the shape the
    /// card's BodyShape line already had before the axes existed. An unset axis is omitted, never defaulted. The chest
    /// label follows <see cref="ChestLabel"/> so a male card says "chest", not "bust".
    /// </summary>
    public string Compose()
    {
        var parts = new List<string>(10);
        AddPart(parts, null, BodyBuild);
        AddPart(parts, null, Silhouette);
        AddPart(parts, null, Adiposity);
        AddPart(parts, null, FatDistribution);
        AddPart(parts, null, MuscleMass);
        AddPart(parts, null, MuscleDefinition);
        AddPart(parts, string.IsNullOrWhiteSpace(ChestLabel) ? "bust" : ChestLabel.Trim(), BustSize);
        AddPart(parts, "waist", WaistSize);
        AddPart(parts, "hips", HipSize);
        AddPart(parts, "rear", ButtSize);
        return string.Join(", ", parts);
    }

    private static void AddPart(List<string> parts, string? label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        parts.Add(string.IsNullOrEmpty(label) ? trimmed : $"{label} {trimmed}");
    }
}

/// <summary>One BodyCard field: its label, whether it is a <c>[DECIDE]</c> item, and how to read and write it.</summary>
public sealed record CharacterBodyCardFieldDefinition(
    CharacterBodyCardField Field,
    string Label,
    bool RequiresDecision,
    Func<CharacterBodyCard, string> Get,
    Action<CharacterBodyCard, string> Set)
{
    public bool IsAnswered(CharacterBodyCard card) => !string.IsNullOrWhiteSpace(Get(card));
}

/// <summary>The canonical field set and order — the one place that defines what a body card contains.</summary>
public static class CharacterBodyCardFields
{
    /// <summary>
    /// Body shape → height/build → skin → body hair → tattoos → scars/marks → pubic hair. Tattoos, body hair
    /// and pubic hair are the capture list's <c>[DECIDE]</c> items.
    /// </summary>
    public static readonly IReadOnlyList<CharacterBodyCardFieldDefinition> All =
    [
        new(CharacterBodyCardField.BodyShape, "Body shape and proportions", false,
            card => card.BodyShape, (card, value) => card.BodyShape = value),
        new(CharacterBodyCardField.HeightBuild, "Height and build", false,
            card => card.HeightBuild, (card, value) => card.HeightBuild = value),
        new(CharacterBodyCardField.Skin, "Skin tone and texture", false,
            card => card.Skin, (card, value) => card.Skin = value),
        new(CharacterBodyCardField.BodyHair, "Body hair (chest, stomach, arms, legs)", true,
            card => card.BodyHair, (card, value) => card.BodyHair = value),
        new(CharacterBodyCardField.Tattoos, "Tattoos (design + exact placement)", true,
            card => card.Tattoos, (card, value) => card.Tattoos = value),
        new(CharacterBodyCardField.ScarsMarks, "Scars and marks", false,
            card => card.ScarsMarks, (card, value) => card.ScarsMarks = value),
        new(CharacterBodyCardField.PubicHair, "Pubic hair", true,
            card => card.PubicHair, (card, value) => card.PubicHair = value)
    ];

    public static CharacterBodyCardFieldDefinition Require(CharacterBodyCardField field)
        => All.FirstOrDefault(definition => definition.Field == field)
            ?? throw new InvalidOperationException($"Unsupported body card field '{field}'.");
}

/// <summary>
/// The body target's prompt-template keys, seeded into the ONE template store under this namespace
/// (B-122 Phase 0). Nothing here is a prompt body — the bodies live in the store, as data.
/// </summary>
public static class CharacterBodyWorkflowKeys
{
    public const string ClothedAcquire = "identity.body.clothed.acquire";
    public const string UnclothedAcquire = "identity.body.unclothed.acquire";
    public const string UnclothedEdit = "identity.body.unclothed.edit";
    public const string AngleThreeQuarterLeft = "identity.body.angle.three-quarter";
    public const string AngleThreeQuarterRight = "identity.body.angle.three-quarter.right";
    public const string AngleProfileLeft = "identity.body.angle.profile";
    public const string AngleProfileRight = "identity.body.angle.profile.right";
    public const string ExtendedView = "identity.body.extended.rotation";
    public const string Normalize = "identity.body.normalize";

    /// <summary>
    /// The camera clauses an ANGLE RENDER attaches to the compiled body prompt: the same body seen from another side,
    /// described to a model rather than asked of an editor. Separate keys from the rotation instructions above,
    /// because a rotation is a request about an image that already exists and a render is a description of a body.
    /// </summary>
    public const string RenderThreeQuarterLeft = "identity.body.angle.render.three-quarter";
    public const string RenderThreeQuarterRight = "identity.body.angle.render.three-quarter.right";
    public const string RenderProfileLeft = "identity.body.angle.render.profile";
    public const string RenderProfileRight = "identity.body.angle.render.profile.right";

    /// <summary>
    /// The camera clause for the BACK angle render (2026-09-24). It has to say that no face is visible: the accepted
    /// body reference is a picture of the FRONT, so "from behind" alone leaves the model free to turn the head and show
    /// the face it was shown.
    /// </summary>
    public const string RenderBack = "identity.body.angle.render.back";

    public static readonly IReadOnlyList<string> All =
    [
        ClothedAcquire,
        UnclothedAcquire,
        UnclothedEdit,
        AngleThreeQuarterLeft,
        AngleThreeQuarterRight,
        AngleProfileLeft,
        AngleProfileRight,
        ExtendedView,
        Normalize,
        RenderThreeQuarterLeft,
        RenderThreeQuarterRight,
        RenderProfileLeft,
        RenderProfileRight,
        RenderBack
    ];
}
