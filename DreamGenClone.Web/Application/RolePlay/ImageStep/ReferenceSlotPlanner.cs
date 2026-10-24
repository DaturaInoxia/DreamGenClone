using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>One reference image a host has resolved and is offering to a slot.</summary>
/// <param name="ReferenceLabel">
/// What the bound image IS, in words, for the operator — "Front · Clothed" rather than the pack's asset id. Carried
/// beside the ids so the step composer can name the reference it is showing without a lookup of its own: the component
/// is host-driven and holds no services, so anything it displays has to arrive as data. Null falls back to the ids.
/// </param>
public sealed record ImageStepSlotSource(
    ImageStepReferenceSourceKind SourceKind,
    string Strategy,
    string? SceneAssetId = null,
    string? SceneAssetImageId = null,
    int? SceneAssetVersion = null,
    string? SceneAssetSha256 = null,
    string? SkeletonRelativePath = null,
    string? PosePresetId = null,
    string? IdentityPackId = null,
    string? ReferenceAssetId = null,
    string? ReferenceLabel = null,

    /// <summary>
    /// The rendered app image this slot supplies, when <see cref="SourceKind"/> is
    /// <c>ImageStepReferenceSourceKind.AppImage</c>, with its checksum as bound. An id space of its own — see
    /// <c>ReferenceApplicationSelection.ReferenceImageId</c> — so it is carried here rather than folded into the
    /// scene-asset fields, which the approved-asset resolver validates against an approval this image never had.
    /// </summary>
    string? ReferenceImageId = null,

    string? ReferenceImageSha256 = null,

    /// <summary>
    /// What the OPERATOR says this reference is doing in this frame (B-159). Carried on the SOURCE rather than added
    /// after planning, because the planner rebuilds a binding from its source: a value that lived only on the binding
    /// would be dropped the next time the operator touched any other control, which is the same shape of defect the
    /// named-argument comment below records.
    /// </summary>
    ReferenceDirections? Directions = null)
{
    /// <summary>
    /// This source as rebuilt from a BINDING — the round-trip a re-plan depends on.
    /// </summary>
    /// <remarks>
    /// It lives with the shape it has to preserve, because a field that is not copied here is a field the NEXT
    /// unrelated edit silently drops: every re-plan (adding another reference, removing one, changing a strategy)
    /// converts the current bindings back to sources and plans again. Omitted, the APP-IMAGE channel disappeared on the
    /// first re-plan and validation refused the result — "Reference application 'Location' declares strategy
    /// 'NativeMultiReference' but names no reference image", reported live 2026-10-18 while binding a Studio render as
    /// a location reference. The named arguments are the protection the previous incident (2026-09-27, a pack id
    /// landing in the pose-preset field) earned.
    /// </remarks>
    public static ImageStepSlotSource FromBinding(ReferenceApplicationSelection binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return new ImageStepSlotSource(
            SourceKind: Enum.TryParse<ImageStepReferenceSourceKind>(
                binding.Source, ignoreCase: true, out var kind)
                ? kind
                : ImageStepReferenceSourceKind.ApprovedSceneAsset,
            Strategy: binding.Strategy,
            SceneAssetId: binding.SceneAssetId,
            SceneAssetImageId: binding.SceneAssetImageId,
            SceneAssetVersion: binding.SceneAssetVersion,
            SceneAssetSha256: binding.SceneAssetSha256,
            SkeletonRelativePath: binding.SkeletonRelativePath,
            PosePresetId: binding.PosePresetId,
            IdentityPackId: binding.IdentityPackId,
            ReferenceAssetId: binding.ReferenceAssetId,
            ReferenceLabel: binding.ReferenceLabel,
            ReferenceImageId: binding.ReferenceImageId,
            ReferenceImageSha256: binding.ReferenceImageSha256,
            Directions: binding.Directions);
    }
}

/// <summary>A slot the host has filled: which slot, for which actor, with which resolved source.</summary>
public sealed record ImageStepSlotAssignment(
    ImageStepSlotKind SlotKind,
    string? ActorKey,
    ImageStepSlotSource Source);

/// <summary>
/// Expands a <see cref="ImageStepBlueprint"/> plus the host's resolved assignments into the ORDERED reference
/// bindings a render consumes.
/// </summary>
/// <remarks>
/// Deliberately pure - no database, no services, no clock - so the decisions that decide what an image is made
/// from can be tested on their own and reviewed line by line. Everything it needs is passed in.
///
/// Four rules, all fail-fast:
/// <list type="number">
/// <item><description>An assignment must name a declared slot, by kind AND actor.</description></item>
/// <item><description>Its source kind must be one the slot allows, because the host declared that set.</description></item>
/// <item><description>A required slot must be filled. A step that cannot be created is refused, not rendered short.</description></item>
/// <item><description>The reference count must fit the budget that applies to THIS step's slots.</description></item>
/// </list>
/// </remarks>
public static class ReferenceSlotPlanner
{
    /// <summary>
    /// The reference count above which a POSE reference stops being honoured. Measured 2026-09-25 (CASE-22): at ten
    /// references the figure ignored a skeleton whichever slot it held - at slot 10 AND at slot 1 - and the anatomy
    /// broke when the skeleton was last. Two, four and six references all followed the skeleton.
    /// </summary>
    /// <remarks>
    /// This is NOT a second copy of the model's <c>MaxReferences</c>. That value is an ACCEPTANCE limit (how many
    /// slots the model takes at all), and a ten-slot graph demonstrably runs with its references consumed - a slot-5
    /// garment transferred. This one is a QUALITY limit that applies only when a skeleton is actually carrying a
    /// pose. Conflating the two is how an untested 16 was written into a model row.
    /// </remarks>
    public const int PoseCarryingReferenceBudget = 6;

    /// <summary>The legacy element key a slot reports as, so bindings stay readable to existing consumers.</summary>
    public static string ElementKeyFor(ImageStepSlotKind slotKind) => slotKind switch
    {
        ImageStepSlotKind.Face => "Identity",
        ImageStepSlotKind.Body => "Body",
        ImageStepSlotKind.Wardrobe => "Wardrobe",
        ImageStepSlotKind.Location => "Location",
        ImageStepSlotKind.Pose => "Pose",
        ImageStepSlotKind.CharacterPose => "CharacterPose",
        ImageStepSlotKind.Prop => "Prop",
        _ => throw new InvalidOperationException($"Reference slot kind '{slotKind}' has no legacy element key.")
    };

    /// <summary>The reference budget that applies to a step, given which slots it actually fills.</summary>
    public static int EffectiveBudget(int maxReferences, IEnumerable<ImageStepSlotKind> filledSlotKinds)
    {
        ArgumentNullException.ThrowIfNull(filledSlotKinds);
        if (maxReferences < 1)
        {
            throw new InvalidOperationException(
                $"A reference budget of {maxReferences} cannot be used: a step that offers references needs at least one. "
                + "Fix the model's MaxReferences in Model Manager (/model-manager).");
        }

        // A Pose slot is the skeleton that carries stance; CharacterPose is a photoreal image and does not depend on
        // pose guidance, so it is not what the measured ceiling is about.
        var carriesPose = filledSlotKinds.Contains(ImageStepSlotKind.Pose);
        return carriesPose ? Math.Min(maxReferences, PoseCarryingReferenceBudget) : maxReferences;
    }

    /// <summary>
    /// The assignment list one bind produces: the slot's references REPLACED by <paramref name="source"/>, or the
    /// source ADDED beside them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one decision about replace-vs-add, taken here rather than in each component so two surfaces cannot
    /// answer it differently. A slot that declares <see cref="ImageStepSlotBlueprint.AllowsMultiple"/> takes another
    /// VIEW of the same thing (a shed bound as Front and Left), which is an ADD. A command that names ONE reference
    /// ("use this image as the location") means the slot holds exactly that image, which is a REPLACE —
    /// <paramref name="asSoleReference"/>.
    /// </para>
    /// <para>
    /// Measured 2026-10-19: the "use as reference" command restored the source image's own recorded bindings (the
    /// place that image was rendered in) and then added the picked image as a second Location reference, so the next
    /// render was anchored to the old place AND the new one. The command says "this image IS the location reference",
    /// so it replaces.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ImageStepSlotAssignment> AssignmentsFor(
        IEnumerable<ImageStepSlotAssignment> current,
        ImageStepSlotBlueprint slot,
        ImageStepSlotSource source,
        bool asSoleReference = false)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(source);

        var assignments = current.ToList();
        if (asSoleReference || !slot.AllowsMultiple)
        {
            assignments.RemoveAll(assignment =>
                assignment.SlotKind == slot.SlotKind
                && string.Equals(
                    assignment.ActorKey ?? string.Empty, slot.ActorKey ?? string.Empty, StringComparison.Ordinal));
        }

        assignments.Add(new ImageStepSlotAssignment(slot.SlotKind, slot.ActorKey, source));
        return assignments;
    }

    /// <summary>
    /// The ordered bindings for a step. Ordinals follow the BLUEPRINT's slot order, so the same step always sends its
    /// references in the same positions - slot order decides placement, and a placement that changed with the order a
    /// caller happened to build its list in would be invisible in the UI.
    /// </summary>
    public static IReadOnlyList<ReferenceApplicationSelection> Plan(
        ImageStepBlueprint blueprint,
        IReadOnlyList<ImageStepSlotAssignment> assignments,
        int maxReferences)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(assignments);

        blueprint.Validate();

        var declared = blueprint.Slots
            .Select(slot => (slot.SlotKind, Key: slot.ActorKey ?? string.Empty, Slot: slot))
            .ToArray();

        // One LIST per slot rather than one assignment: a slot that declares AllowsMultiple (a wardrobe, where the
        // operator may name the dress AND the shoes) accepts several references, and every other slot still refuses a
        // second one. That refusal is what stands between "the operator bound a second image" and "the render used the
        // first and silently dropped the rest", so it is kept for every slot that does not opt in.
        var bySlot = new Dictionary<(ImageStepSlotKind, string), List<ImageStepSlotAssignment>>();
        foreach (var assignment in assignments)
        {
            var key = (assignment.SlotKind, assignment.ActorKey ?? string.Empty);
            var allowsMultiple = declared
                .FirstOrDefault(entry => entry.SlotKind == key.Item1 && string.Equals(entry.Key, key.Item2, StringComparison.Ordinal))
                .Slot?.AllowsMultiple ?? false;
            if (!bySlot.TryGetValue(key, out var slotAssignments))
            {
                slotAssignments = [];
                bySlot[key] = slotAssignments;
            }
            else if (!allowsMultiple)
            {
                throw new InvalidOperationException(
                    $"Slot '{assignment.SlotKind}'"
                    + (string.IsNullOrWhiteSpace(assignment.ActorKey) ? string.Empty : $" for actor '{assignment.ActorKey}'")
                    + " was filled more than once for one step.");
            }

            slotAssignments.Add(assignment);
        }

        foreach (var assignment in assignments)
        {
            var match = declared.FirstOrDefault(entry =>
                entry.SlotKind == assignment.SlotKind
                && string.Equals(entry.Key, assignment.ActorKey ?? string.Empty, StringComparison.Ordinal));
            if (match.Slot is null)
            {
                throw new InvalidOperationException(
                    $"This step declares no '{assignment.SlotKind}' slot"
                    + (string.IsNullOrWhiteSpace(assignment.ActorKey) ? string.Empty : $" for actor '{assignment.ActorKey}'")
                    + ", so its reference cannot be bound.");
            }

            if (!match.Slot.AllowedSources.Contains(assignment.Source.SourceKind))
            {
                throw new InvalidOperationException(
                    $"Slot '{assignment.SlotKind}' does not accept a '{assignment.Source.SourceKind}' reference. "
                    + $"It accepts: {string.Join(", ", match.Slot.AllowedSources)}.");
            }

            if (string.IsNullOrWhiteSpace(assignment.Source.Strategy))
            {
                throw new InvalidOperationException(
                    $"The '{assignment.SlotKind}' reference has no strategy. Every bound reference needs one, because an "
                    + "unqualified strategy fails at render time instead of being silently downgraded.");
            }
        }

        var unfilledRequired = declared
            .Where(entry => entry.Slot.Required && !bySlot.ContainsKey((entry.SlotKind, entry.Key)))
            .ToArray();
        if (unfilledRequired.Length > 0)
        {
            throw new InvalidOperationException(
                "This step cannot be created because required reference slot(s) are empty: "
                + string.Join(", ", unfilledRequired.Select(entry =>
                    string.IsNullOrEmpty(entry.Key) ? entry.SlotKind.ToString() : $"{entry.SlotKind} for '{entry.Key}'"))
                + ".");
        }

        var budget = EffectiveBudget(maxReferences, assignments.Select(assignment => assignment.SlotKind));
        if (assignments.Count > budget)
        {
            var carriesPose = assignments.Any(assignment => assignment.SlotKind == ImageStepSlotKind.Pose);
            throw new InvalidOperationException(
                $"This step binds {assignments.Count} references but only {budget} are usable"
                + (carriesPose && budget == PoseCarryingReferenceBudget
                    ? $": above {PoseCarryingReferenceBudget} references a POSE reference stops being honoured, whichever "
                        + "position it holds (measured 2026-09-25, CASE-22). Remove references, or move the pose to its own step."
                    : $". Remove references, or raise the model's MaxReferences in Model Manager (/model-manager)."));
        }

        var bindings = new List<ReferenceApplicationSelection>(assignments.Count);
        foreach (var entry in declared)
        {
            if (!bySlot.TryGetValue((entry.SlotKind, entry.Key), out var slotAssignments))
            {
                continue;
            }

            // A multi slot keeps the order the host supplied its images in, so the first one the operator bound stays
            // the first reference the model sees (slot order decides placement).
            foreach (var assignment in slotAssignments)
            {
                bindings.Add(new ReferenceApplicationSelection
                {
                    ElementKey = ElementKeyFor(entry.SlotKind),
                    Kind = entry.SlotKind.ToString(),
                    ActorKey = entry.Key.Length == 0 ? null : entry.Key,
                    SemanticRole = SemanticRoleFor(entry.SlotKind),
                    Source = assignment.Source.SourceKind.ToString(),
                    Strategy = assignment.Source.Strategy,
                    SceneAssetId = assignment.Source.SceneAssetId,
                    SceneAssetImageId = assignment.Source.SceneAssetImageId,
                    SceneAssetVersion = assignment.Source.SceneAssetVersion,
                    SceneAssetSha256 = assignment.Source.SceneAssetSha256,
                    SkeletonRelativePath = assignment.Source.SkeletonRelativePath,
                    PosePresetId = assignment.Source.PosePresetId,
                    IdentityPackId = assignment.Source.IdentityPackId,
                    ReferenceAssetId = assignment.Source.ReferenceAssetId,
                    // A rendered app image travels by its own id and checksum, in its own fields: the approved-asset
                    // fields it does NOT fill are what the approved-asset resolver validates an approval against.
                    ReferenceImageId = assignment.Source.ReferenceImageId,
                    ReferenceImageSha256 = assignment.Source.ReferenceImageSha256,
                    // The operator-facing name of the bound image travels with the ids, so the surfaces that show a
                    // binding can say "Front · Clothed" without holding the identity service themselves.
                    ReferenceLabel = assignment.Source.ReferenceLabel,
                    // The operator's own statement about this reference travels with it, so it survives every re-plan
                    // the composer performs and reaches the render that assigns it a tag (B-159).
                    Directions = assignment.Source.Directions,
                    Ordinal = bindings.Count + 1
                });
            }
        }

        return bindings;
    }

    private static string SemanticRoleFor(ImageStepSlotKind slotKind) => slotKind switch
    {
        ImageStepSlotKind.Face => "character identity",
        ImageStepSlotKind.Body => "character body",
        ImageStepSlotKind.Wardrobe => "wardrobe continuity",
        ImageStepSlotKind.Location => "location continuity",
        ImageStepSlotKind.Pose => "stance",
        ImageStepSlotKind.CharacterPose => "character pose",
        ImageStepSlotKind.Prop => "object in frame",
        _ => throw new InvalidOperationException($"Reference slot kind '{slotKind}' has no semantic role.")
    };
}
