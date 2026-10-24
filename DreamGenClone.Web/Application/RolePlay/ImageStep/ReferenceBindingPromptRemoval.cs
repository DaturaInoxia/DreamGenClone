using DreamGenClone.Domain.ModelManager;
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
        IReadOnlyList<ReferenceApplicationSelection>? bindings,
        IReadOnlyList<ScenePromptFieldOverride>? additionalDerived = null)
    {
        var derived = Derive(bindings);
        if (additionalDerived is { Count: > 0 })
        {
            derived = [.. derived, .. additionalDerived];
        }

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
            if (SlotKindFor(binding) is not { } slotKind)
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

    /// <summary>
    /// The removals a character LoRA implies (B-166 D6). A LoRA trained on a character carries that character's
    /// identity the way a face reference does, so the prompt must stop describing the appearance the model is already
    /// being conditioned on — otherwise the text competes with the LoRA and the render drifts between the two.
    ///
    /// <para>
    /// Only the FACE element is removed: clothing stays text-controlled until it is measured (D6). The key is the
    /// character's PROFILE id, which is the same key the compiled brief and the appearance override map use.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ScenePromptFieldOverride> DeriveForCharacterLoras(
        IReadOnlyList<ResolvedCharacterLora>? loras)
    {
        if (loras is not { Count: > 0 })
        {
            return [];
        }

        var fields = new List<ScenePromptFieldOverride>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lora in loras)
        {
            var key = lora.CharacterProfileId?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    $"Character LoRA artifact '{lora.ArtifactId}' resolves to no character, so the prompt cannot know "
                    + "whose appearance the LoRA supplies. Fix the artifact's character ownership.");
            }

            foreach (var scope in StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Face, key))
            {
                if (seen.Add(scope))
                {
                    fields.Add(new ScenePromptFieldOverride { ElementKey = scope, Removed = true });
                }
            }
        }

        return fields;
    }

    /// <summary>
    /// Whether this binding takes a prompt element over at all - the one filter <see cref="Derive"/> applies and the
    /// one the caller must apply before it may remove anything, so a caller cannot act on a binding this refuses.
    ///
    /// <para>
    /// Two rules decide it. A binding that supplies no image replaces nothing (a skeleton counts as supplying one: it
    /// is a reference image like any other, and it is what makes the stance prose redundant), and a binding whose
    /// strategy is <c>TextOnly</c> supplies nothing either however much it carries. Both are the SAME rules
    /// <see cref="ReferenceApplicationSelection.SuppliesImage"/> and the reference rules state elsewhere.
    /// </para>
    /// </summary>
    public static bool ImpliesRemoval(ReferenceApplicationSelection binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return SlotKindFor(binding) is not null;
    }

    /// <summary>
    /// The slot a binding removes prose for, or null when it removes nothing: it supplies no image, its strategy means
    /// text rather than an image, it names no slot kind, or it is a per-character element with no character to address.
    /// </summary>
    /// <remarks>
    /// A per-character element can only be addressed when the binding names the character. Until the caller supplies
    /// an actor key there is nothing to point the removal at, and guessing which character the operator meant is
    /// exactly the kind of silent substitution the RP rules forbid - so this derives nothing rather than removing the
    /// wrong prose.
    /// </remarks>
    private static ImageStepSlotKind? SlotKindFor(ReferenceApplicationSelection binding)
    {
        if (!binding.SuppliesImage
            || string.Equals(binding.Strategy, "TextOnly", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (ReferenceBindingShape.SlotKindOf(binding) is not { } slotKind)
        {
            return null;
        }

        if (ImageStepSlotBlueprint.ActorKeyRequirementFor(slotKind) == ImageStepActorKeyRequirement.Required
            && string.IsNullOrWhiteSpace(binding.ActorKey))
        {
            return null;
        }

        return slotKind;
    }
}
