using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Turns the reference bindings a step will render with into prompt REMOVALS, so the generated prompt does not
/// describe an element an image already supplies.
/// </summary>
/// <remarks>
/// This is the join between the composer and the prompt generator, and it deliberately reuses the machinery a
/// user-authored removal already travels through (<see cref="ScenePromptOverridesApplier"/> and its
/// <c>ScenePromptRemovalNotice</c>): the compiler sees exactly one value per element either way, and the removal
/// is stated to the model as authoritative rather than merely omitted. A second mechanism would be a second set
/// of semantics to keep in step.
///
/// Two rules:
/// <list type="bullet">
/// <item><description>
/// <b>A user override always wins.</b> If the operator has written a value or removed a field, a binding must not
/// change it - the bindings supply DEFAULT behaviour, never a correction to a deliberate edit.
/// </description></item>
/// <item><description>
/// <b>A text-only binding contributes nothing.</b> It describes no image, so there is nothing for it to replace.
/// </description></item>
/// </list>
/// </remarks>
public static class ReferenceBindingPromptRemoval
{
    /// <summary>
    /// The overrides to apply, with binding-derived removals merged in behind the operator's own.
    /// Returns the operator's overrides unchanged when there is nothing to derive, so the common case allocates
    /// nothing and cannot alter behaviour.
    /// </summary>
    public static ScenePromptOverrides? Merge(
        ScenePromptOverrides? userOverrides,
        IReadOnlyList<ReferenceApplicationSelection>? bindings)
    {
        var derived = Derive(bindings);
        if (derived.Count == 0)
        {
            return userOverrides;
        }

        var merged = new ScenePromptOverrides
        {
            Fields = [.. (userOverrides?.Fields ?? [])],
            RemovedCharacters = [.. (userOverrides?.RemovedCharacters ?? [])]
        };

        var claimed = new HashSet<string>(merged.Fields.Select(field => field.ElementKey), StringComparer.OrdinalIgnoreCase);
        foreach (var field in derived)
        {
            if (claimed.Add(field.ElementKey))
            {
                merged.Fields.Add(field);
            }
        }

        return merged;
    }

    /// <summary>
    /// The removals a set of bindings implies, in the order the bindings were given.
    /// </summary>
    public static IReadOnlyList<ScenePromptFieldOverride> Derive(
        IReadOnlyList<ReferenceApplicationSelection>? bindings)
    {
        if (bindings is null || bindings.Count == 0)
        {
            return [];
        }

        var fields = new List<ScenePromptFieldOverride>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in bindings)
        {
            // A binding that supplies no image replaces nothing. A skeleton counts as supplying one: it is a reference
            // image like any other, and it is what makes the stance prose redundant.
            if (!binding.SuppliesImage
                || string.Equals(binding.Strategy, "TextOnly", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ReferenceBindingShape.SlotKindOf(binding) is not { } slotKind)
            {
                continue;
            }

            var requirement = ImageStepSlotBlueprint.ActorKeyRequirementFor(slotKind);

            // A per-character element can only be addressed when the binding names the character. Until the caller
            // supplies an actor key there is nothing to point the removal at, and guessing which character the
            // operator meant is exactly the kind of silent substitution the RP rules forbid - so this derives
            // nothing rather than removing the wrong prose. B130-011 populates actor keys from the Moment cast,
            // which is what lights these up.
            if (requirement == ImageStepActorKeyRequirement.Required && string.IsNullOrWhiteSpace(binding.ActorKey))
            {
                continue;
            }

            foreach (var scope in StepPromptElementPlan.ScopesFor(slotKind, binding.ActorKey))
            {
                if (seen.Add(scope))
                {
                    fields.Add(new ScenePromptFieldOverride { ElementKey = scope, Removed = true });
                }
            }
        }

        return fields;
    }
}
