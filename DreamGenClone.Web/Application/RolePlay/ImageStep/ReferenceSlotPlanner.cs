using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>One reference image a host has resolved and is offering to a slot.</summary>
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
    string? ReferenceAssetId = null);

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

        var bySlot = new Dictionary<(ImageStepSlotKind, string), ImageStepSlotAssignment>();
        foreach (var assignment in assignments)
        {
            if (!bySlot.TryAdd((assignment.SlotKind, assignment.ActorKey ?? string.Empty), assignment))
            {
                throw new InvalidOperationException(
                    $"Slot '{assignment.SlotKind}'"
                    + (string.IsNullOrWhiteSpace(assignment.ActorKey) ? string.Empty : $" for actor '{assignment.ActorKey}'")
                    + " was filled more than once for one step.");
            }
        }

        var declared = blueprint.Slots
            .Select(slot => (slot.SlotKind, Key: slot.ActorKey ?? string.Empty, Slot: slot))
            .ToArray();

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

        var bindings = new List<ReferenceApplicationSelection>(declared.Length);
        foreach (var entry in declared)
        {
            if (!bySlot.TryGetValue((entry.SlotKind, entry.Key), out var assignment))
            {
                continue;
            }

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
                Ordinal = bindings.Count + 1
            });
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
        _ => throw new InvalidOperationException($"Reference slot kind '{slotKind}' has no semantic role.")
    };
}
