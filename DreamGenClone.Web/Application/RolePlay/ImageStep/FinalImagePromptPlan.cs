using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>One entry of the reference map: which tag is which subject, slot and position.</summary>
/// <param name="Index">The model's own image number (1-based, including any leading source image).</param>
/// <param name="Tag">The tag the prompt must use — <c>&lt;imageN&gt;</c>, or <see cref="ReferenceRoleClauses.UntaggedImageLabel"/> when the step sends a single image and no tag exists.</param>
/// <param name="SubjectKey">The participant profile key this reference belongs to, or null for a scene-level slot.</param>
/// <param name="Position">Where that subject sits in the frame, or null when the frame does not place it.</param>
public sealed record ImagePromptReferenceEntry(
    int Index,
    string Tag,
    string? SubjectKey,
    ImageStepSlotKind? SlotKind,
    FramePosition? Position);

/// <summary>
/// The assembled final prompt plus the map that says what each <c>&lt;imageN&gt;</c> is (B-157).
///
/// <para>
/// The text itself is owned by <see cref="ReferenceRoleClauses"/> — this type does not re-implement the clause, it
/// assembles and validates. That is the point: the numbering, the clause and the map are produced from ONE ordered
/// list, so a tag can never point at a different image than the sentence beside it describes.
/// </para>
/// </summary>
public sealed record FinalImagePromptPlan(
    string Prompt,
    IReadOnlyList<ImagePromptReferenceEntry> References,
    IReadOnlyList<string> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// The single place a final image prompt is assembled and validated (B-157).
///
/// <para>
/// Before this, five handlers each appended the role block in their own way, so "which image is which" was decided in
/// five places and shown to nobody. Assembly here is deliberately strict: <see cref="Build"/> REFUSES a plan whose
/// map is contradictory rather than emitting a prompt whose tags disagree with its sentences, because a wrong tag is
/// invisible in the finished image — it simply produces a render nobody asked for that looks like one that honoured
/// the request.
/// </para>
/// </summary>
public static class FinalImagePromptPlanBuilder
{
    /// <summary>
    /// Assembles the final prompt and its reference map, refusing anything inconsistent.
    /// </summary>
    /// <param name="prompt">The compiled prose the block is appended to.</param>
    /// <param name="rolesInSendOrder">
    /// The references as they will be sent, in send order. A single reference is never TAGGED (the vendor's own
    /// N = 1 rule): it is described without a number when something is stated about it, and the map shows it under
    /// <see cref="ReferenceRoleClauses.UntaggedImageLabel"/>.
    /// </param>
    /// <param name="leadingImages">Source images the model receives before these; 1 for an edit.</param>
    /// <param name="blocking">The brief's frame-space blocking, or null when none was derived.</param>
    /// <param name="maxReferences">
    /// The model's acceptance limit, or null when the caller has not resolved one. Never defaulted here: the budget
    /// owner is <see cref="ReferenceSlotPlanner.EffectiveBudget"/>.
    /// </param>
    /// <param name="filledSlotKinds">The slot kinds this step actually fills, which decides the pose-carrying budget.</param>
    public static FinalImagePromptPlan Build(
        string prompt,
        IReadOnlyList<ReferenceRoleClauses.ReferenceRole>? rolesInSendOrder,
        int leadingImages = ReferenceRoleClauses.LeadingImageCount.Generate,
        SceneBlocking? blocking = null,
        int? maxReferences = null,
        IReadOnlyList<ImageStepSlotKind>? filledSlotKinds = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var roles = rolesInSendOrder ?? [];
        var issues = Validate(roles, leadingImages, blocking, maxReferences, filledSlotKinds);
        if (issues.Count != 0)
        {
            throw new InvalidOperationException(
                "The final image prompt is refused because its reference map is inconsistent: "
                + string.Join(" ", issues));
        }

        var text = ReferenceRoleClauses.AppendToImagePrompt(prompt, roles, leadingImages, blocking);

        // The frame's camera is stated LAST, after the reference block. It used to reach the model only as a JSON field
        // the prompt-writing model might echo, which is how a render framed at one distance came back with everyone at
        // close range (B-161). Stating it here gives the configured framing the same authority the reference roles
        // already have, and gives depth words a scale to mean something against.
        //
        // ONE camera statement per prompt: a compiled prompt may already carry it (the POV projection states the camera
        // for a participant POV, and the operator's framing is appended deterministically). Appending a second, from the
        // compiled frame, put two different cameras in one prompt and the lower one won — measured 2026-10-21, where the
        // bottom line was always the profile's "medium shot" while the control read "close shot".
        var framing = text.Contains(FramingMarker, StringComparison.Ordinal)
            ? string.Empty
            : SceneFramingClause.LineFor(blocking);
        if (framing.Length > 0)
        {
            text = $"{text.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{framing}";
        }

        return new FinalImagePromptPlan(text, BuildMap(roles, leadingImages, blocking), issues);
    }

    /// <summary>
    /// The marker that identifies a prompt which already states its camera. A prompt carries ONE camera statement: the
    /// compiled prompt may own it (a participant POV states it in the projection), and a second one appended below it
    /// overrides the first — which is exactly what happened when the lower copy was always the profile default.
    /// </summary>
    public const string FramingMarker = "CAMERA — AUTHORITATIVE";

    /// <summary>
    /// The consistency rules, as data rather than as a throw — so the composer preview can SHOW an operator what is
    /// wrong before a render is attempted. <see cref="Build"/> refuses on the same list.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        IReadOnlyList<ReferenceRoleClauses.ReferenceRole>? rolesInSendOrder,
        int leadingImages = ReferenceRoleClauses.LeadingImageCount.Generate,
        SceneBlocking? blocking = null,
        int? maxReferences = null,
        IReadOnlyList<ImageStepSlotKind>? filledSlotKinds = null)
    {
        var roles = rolesInSendOrder ?? [];
        var issues = new List<string>();
        if (!ReferenceRoleClauses.SendsTags(roles.Count, leadingImages))
        {
            // The model receives ONE image in total, so it is never numbered and there is no TAGGED map to contradict.
            // Whether it is described is decided by the clause owner (ReferenceRoleClauses.CarriesStatement) —
            // deliberately not re-decided here.
            return issues;
        }

        if (leadingImages < 0)
        {
            issues.Add(
                $"A leading-image count of {leadingImages} would number the first reference before <image1>.");
        }

        if (blocking is not null)
        {
            var duplicatePositions = blocking.Subjects
                .GroupBy(subject => subject.Position)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            foreach (var position in duplicatePositions)
            {
                issues.Add(
                    $"Two subjects share the frame position {position.Horizontal}/{position.Depth}, so the clause "
                    + "would claim one place belongs to two people.");
            }

            // A reference whose subject the frame does not place is deliberately NOT refused: the clause falls back to
            // the scene-level sentence for it, and the map records a null position so the operator can see the
            // mismatch. Refusing here would break renders that work today for a mismatch that degrades rather than
            // misleads — the failure mode has to be visible, not fatal.
        }

        // One reference per slot per subject: a slot bound twice for one person is what makes the model pick.
        foreach (var group in roles
            .Where(role => !string.IsNullOrWhiteSpace(role.SubjectKey) && role.SlotKind is not null)
            .GroupBy(role => (Key: role.SubjectKey!, role.SlotKind))
            .Where(group => group.Count() > 1))
        {
            issues.Add(
                $"Subject '{group.Key.Key}' supplies the {group.Key.SlotKind} slot {group.Count()} times.");
        }

        if (maxReferences is { } limit)
        {
            var budget = ReferenceSlotPlanner.EffectiveBudget(limit, filledSlotKinds ?? roles
                .Where(role => role.SlotKind is not null)
                .Select(role => role.SlotKind!.Value));
            if (roles.Count > budget)
            {
                issues.Add(
                    $"{roles.Count} references are bound but this step's budget is {budget}"
                    + (budget < limit ? " (a pose reference is carried)" : string.Empty)
                    + ". Remove references, or raise the model's MaxReferences in Model Manager.");
            }
        }

        return issues;
    }

    /// <summary>
    /// Problems with the <c>&lt;imageN&gt;</c> mentions in the prompt a render will start from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing validated these before (B-159). The clause and the prompt are two halves of one request, and until now
    /// only the clause was checked — so a prompt could name <c>&lt;image5&gt;</c> when three images were sent, or name
    /// <c>&lt;image1&gt;</c> when the model receives no tags at all, and the render would proceed with a description
    /// bound to an image that is not there. That became reachable the moment placement had to be written by hand
    /// (2026-10-13), which is exactly the situation an operator has no way to check for themselves.
    /// </para>
    ///
    /// <para>
    /// It reports rather than throws, on the same principle the frame-position mismatch uses: this degrades a render
    /// rather than producing a wrong one silently, so the operator is told and the render still runs. The block is
    /// APPENDED after this text, so a tag the clause itself emits is not counted here — only what the prompt says.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> PromptTagIssues(
        string? prompt,
        IReadOnlyList<ReferenceRoleClauses.ReferenceRole>? rolesInSendOrder,
        int leadingImages = ReferenceRoleClauses.LeadingImageCount.Generate)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return issues;
        }

        var sent = rolesInSendOrder?.Count ?? 0;
        var firstTag = leadingImages + 1;
        var lastTag = leadingImages + sent;

        foreach (var mention in System.Text.RegularExpressions.Regex
            .Matches(prompt, @"<image(\d+)>")
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(number => number))
        {
            if (!ReferenceRoleClauses.SendsTags(sent, leadingImages))
            {
                issues.Add(
                    $"The prompt names <image{mention}> but this run sends {sent} reference "
                    + $"{(sent == 1 ? "image" : "images")}, and a run that sends only ONE image in total gets no tags "
                    + "at all.");
                continue;
            }

            if (mention < firstTag || mention > lastTag)
            {
                issues.Add(
                    $"The prompt names <image{mention}>, but this render sends {sent} reference images numbered "
                    + $"<image{firstTag}> to <image{lastTag}>"
                    + (leadingImages > 0 ? " (its source image is slot 1)" : string.Empty)
                    + ". Bind another reference, or correct the number.");
            }
        }

        return issues;
    }

    private static IReadOnlyList<ImagePromptReferenceEntry> BuildMap(
        IReadOnlyList<ReferenceRoleClauses.ReferenceRole> roles,
        int leadingImages,
        SceneBlocking? blocking)
    {
        if (roles.Count == 0)
        {
            return [];
        }

        if (!ReferenceRoleClauses.SendsTags(roles.Count, leadingImages))
        {
            // ONE image in total: no tag exists to show, so the row names the image instead of numbering it — and it is
            // shown only when the clause owner actually describes it, so the map can never promise text the prompt lacks.
            return ReferenceRoleClauses.CarriesStatement(roles[0])
                ? [new ImagePromptReferenceEntry(
                    leadingImages + 1,
                    ReferenceRoleClauses.UntaggedImageLabel,
                    roles[0].SubjectKey,
                    roles[0].SlotKind,
                    PositionFor(roles[0], blocking))]
                : [];
        }

        var entries = new List<ImagePromptReferenceEntry>(roles.Count);
        for (var index = 0; index < roles.Count; index++)
        {
            var role = roles[index];
            entries.Add(new ImagePromptReferenceEntry(
                index + 1 + leadingImages,
                ReferenceRoleClauses.TagFor(index + 1 + leadingImages),
                role.SubjectKey,
                role.SlotKind,
                PositionFor(role, blocking)));
        }

        return entries;
    }

    /// <summary>Where the frame puts this reference's subject, or null when the frame does not place it.</summary>
    private static FramePosition? PositionFor(ReferenceRoleClauses.ReferenceRole role, SceneBlocking? blocking)
        => blocking is null
            ? null
            : blocking.Subjects
                .Where(subject => !string.IsNullOrWhiteSpace(role.SubjectKey)
                    && string.Equals(subject.SubjectKey, role.SubjectKey, StringComparison.OrdinalIgnoreCase))
                .Select(subject => (FramePosition?)subject.Position)
                .FirstOrDefault();

    /// <summary>
    /// Assembles an EDIT instruction's role block.
    ///
    /// <para>
    /// An edit has no compiled brief, so it carries no frame-space blocking — B-157 defers edit-side blocking, because
    /// an edit's instruction is operator-authored and already names its subjects. It still goes through this owner so
    /// the numbering offset (the source image is slot 1) has ONE definition and the block still comes from the clause
    /// owner on the same path as a render.
    /// </para>
    /// </summary>
    public static string BuildEditInstruction(
        string instruction,
        IReadOnlyList<(ImageStepSlotKind? SlotKind, string Description, string? ContainerKey, ReferenceDirections? Directions)> referencesInSendOrder)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        return ReferenceRoleClauses.AppendToEditInstruction(instruction, referencesInSendOrder);
    }
}
