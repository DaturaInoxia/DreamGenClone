namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Everything a still-image prompt must say about one (moment, POV), as TYPED facts rather than prose.
/// </summary>
/// <remarks>
/// <para>
/// This record is the single owner of a prompt's meaning. Until it existed, five layers could each re-word the same
/// fact — the brief, the POV projection, the compiler's fact sheet, a small LLM, and a prose validator — and every
/// failure in B-167 was one of them doing so: "behind" became "in front", the holder's hands became the subject's, a
/// wardrobe was deleted to fit a character budget, a crop was invented and obeyed. A fact has no better and worse
/// phrasing; it has a correct one, so the plan holds it and a family-specific renderer states it.
/// </para>
/// <para>
/// The plan is deliberately inspectable and serialisable: a composer can show it, a test can assert it, and a render can
/// store it, which is what turns "what did this prompt mean" from a question about prose into a question about data.
/// Only <see cref="ActionLine"/> is free text, because it is the one fact whose wording carries meaning; a model may
/// phrase it, and its result is accepted only if it round-trips back into that slot.
/// </para>
/// </remarks>
public sealed record PromptPlan
{
    /// <summary>The moment's single action sentence — the only prose fact in the plan.</summary>
    public string? ActionLine { get; init; }

    /// <summary>Where in the setting the action happens ("at the workbench"), or null when unrecorded.</summary>
    public string? ActionAnchor { get; init; }

    /// <summary>The recorded place in words, or null when the brief records none.</summary>
    public string? Setting { get; init; }

    /// <summary>The place named by its KIND only ("a shed"); the bound location image owns its structure.</summary>
    public string? SettingKind { get; init; }

    /// <summary>
    /// True when a bound location reference supplies the room, so the renderer states the KIND of place and never its
    /// structure. The fact is carried so the decision is data on the plan rather than a rule each renderer re-derives.
    /// </summary>
    public bool SettingSuppliedByReference { get; init; }

    /// <summary>Recorded time of day, or null when the brief does not record one.</summary>
    public string? TimeOfDay { get; init; }

    /// <summary>The moment's recorded temporal anchor ("just before dawn"), or null.</summary>
    public string? TemporalAnchor { get; init; }

    /// <summary>Recorded affect of the instant ("Clenched"), or null.</summary>
    public string? Mood { get; init; }

    /// <summary>The recorded props present in the setting, each named once.</summary>
    public IReadOnlyList<string> Objects { get; init; } = [];


    /// <summary>
    /// Recorded lighting. Owned by the operator, never suppressed by a location reference: the same room under a
    /// different light is a different photograph.
    /// </summary>
    public string? Lighting { get; init; }

    /// <summary>Recorded environment/atmosphere, on the same footing as <see cref="Lighting"/>.</summary>
    public string? Atmosphere { get; init; }

    /// <summary>The people the frame shows, each with its own facts. Never includes the camera holder.</summary>
    public IReadOnlyList<PromptPlanSubject> Subjects { get; init; } = [];

    /// <summary>Body-to-body contact, with OWNERSHIP as a field so a part cannot change owner in prose.</summary>
    public IReadOnlyList<PromptPlanContact> Contacts { get; init; } = [];

    /// <summary>The camera holder's own body, or null when the frame shows none of it.</summary>
    public PromptPlanHolderBody? HolderBody { get; init; }

    /// <summary>How the frame is taken, including what it must CONTAIN.</summary>
    public PromptPlanFrame Frame { get; init; } = new();

    /// <summary>
    /// The coverage set: every fact class the realised prompt must contain. A render is refused when one is missing,
    /// which replaces "did some word from the note appear somewhere" as the test of a complete prompt.
    /// </summary>
    public IReadOnlyList<string> MustAppear { get; init; } = [];

    /// <summary>
    /// The element keys the OPERATOR edited, so a renderer can mark intent and a test can prove an edit reached the
    /// plan — and that nothing else did.
    /// </summary>
    public IReadOnlyList<string> OperatorEditedKeys { get; init; } = [];

    /// <summary>True when nothing about this plan can be realised — no people and no action.</summary>
    public bool IsEmpty => Subjects.Count == 0 && string.IsNullOrWhiteSpace(ActionLine);
}

/// <summary>One person the frame shows, as facts.</summary>
/// <param name="ProfileKey">The brief's participant key (<c>p0</c>) — an identifier, never a prompt word.</param>
/// <param name="Name">The recorded name, which is the word a prompt may use.</param>
/// <param name="Appearance">
/// The recorded appearance prose, or null when a bound face/body reference supplies it (then it must NOT be written).
/// </param>
/// <param name="Wardrobe">What the body is wearing, or its nude/bare state. Always affirmative; never a removed garment.</param>
/// <param name="WorldPose">The body's pose in world terms ("bent over the workbench"), independent of any camera.</param>
/// <param name="Facing">What the body faces in world terms, or null when the record does not say.</param>
/// <param name="FramePlacement">Where they sit in the picture ("LEFT foreground"), or null when unplaced.</param>
/// <param name="InFrameExtent">
/// How much of the body is in frame ("crown to feet"). Derived whole-body by default and operator-overridable; a shot
/// size never decides it.
/// </param>
public sealed record PromptPlanSubject(
    string ProfileKey,
    string Name,
    string? Appearance,
    string Wardrobe,
    string WorldPose,
    string? Facing,
    string? FramePlacement,
    string InFrameExtent)
{
    /// <summary>What this person is doing in this instant, when the record states it separately from the moment.</summary>
    public string? Action { get; init; }

    /// <summary>Where their gaze is directed ("down toward her own forearm"), or null.</summary>
    public string? Sightline { get; init; }

    /// <summary>Where in the setting this person stands ("At the workbench inside the shed"), or null.</summary>
    public string? Place { get; init; }
}

/// <summary>One contact, both ends named by owner so attribution cannot drift.</summary>
public sealed record PromptPlanContact(
    string FromProfileKey,
    string FromName,
    string Part,
    string ToProfileKey,
    string ToName,
    string TargetPart,
    string Manner)
{
    /// <summary>One body touching itself — one statement about one person's own body.</summary>
    public bool IsSelfContact => string.Equals(FromProfileKey, ToProfileKey, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The camera holder's visible body: the parts in frame and what covers them.</summary>
public sealed record PromptPlanHolderBody(IReadOnlyList<string> Parts, string? Wardrobe);

/// <summary>How the frame is taken, and what it must contain.</summary>
/// <param name="ShotSize">The configured shot size ("close shot"), or null when none was derived.</param>
/// <param name="Angle">The recorded angle, or null.</param>
/// <param name="Height">One of <see cref="CameraHeights.All"/>, or null when the brief records none.</param>
/// <param name="Inclusion">
/// The extent common to EVERY subject ("crown to feet"), or null when the subjects do not share one — then each
/// subject's own <see cref="PromptPlanSubject.InFrameExtent"/> governs and the frame states no global figure. Stated in
/// every prompt: a shot size describes how much of the picture a subject occupies and never removes a body part.
/// </param>
/// <param name="FirstPerson">
/// True for a participant POV, where the camera is a person in the scene and the caption must still be written in the
/// third person.
/// </param>
public sealed record PromptPlanFrame(
    string? ShotSize = null,
    string? Angle = null,
    string? Height = null,
    string? Inclusion = FrameInclusionExtent.WholeBody,
    bool FirstPerson = false);

/// <summary>The extent vocabulary a frame states. One table, so "what is in frame" is never a shot-size side effect.</summary>
public static class FrameInclusionExtent
{
    /// <summary>The default extent: the whole person, for every shot size.</summary>
    public const string WholeBody = "crown to feet";

    /// <summary>Every extent a plan may state.</summary>
    public static IReadOnlyList<string> All { get; } = [WholeBody, "hips to head", "waist up", "head and shoulders"];

    /// <summary>Validates a stored/selected extent and returns it trimmed, failing fast on anything else.</summary>
    public static string Require(string? extent, string source)
    {
        if (string.IsNullOrWhiteSpace(extent))
        {
            throw new InvalidOperationException($"{source} names no in-frame extent.");
        }

        var value = extent.Trim();
        if (!All.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{source} names in-frame extent '{value}', which is not one of the recorded extents "
                + $"({string.Join(", ", All)}).");
        }

        return value.ToLowerInvariant();
    }
}
