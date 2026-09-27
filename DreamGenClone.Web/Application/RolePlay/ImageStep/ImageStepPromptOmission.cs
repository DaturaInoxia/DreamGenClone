using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// The ONE answer to "which prompt elements do this step's bound reference images take over?" (D4).
/// </summary>
/// <remarks>
/// Both halves of D4 read this: the composer shows the operator what the images supply, and the code that COMPOSES
/// the prompt removes those elements. They must not be allowed to disagree - a badge that claims an element was
/// removed while the prompt still describes it, or a prompt that silently drops something the operator was never
/// told about, are the same defect from two sides. So there is one function, and every caller supplies the same two
/// inputs it already has (the blueprint and the bindings).
///
/// A binding only counts when it <see cref="ReferenceApplicationSelection.SuppliesImage"/>: a text-only binding
/// describes no image, so there is nothing for it to replace. That is the same rule
/// <see cref="ReferenceBindingPromptRemoval"/> applies on the compiled-brief path, so the template path and the
/// brief path cannot pull in different directions.
/// </remarks>
public static class ImageStepPromptOmission
{
    /// <summary>
    /// The payload element keys the bound images supply, de-duplicated and in slot order. A face and a body image
    /// for the same character yield ONE appearance entry, because a second identical removal is not a second fact.
    /// </summary>
    public static IReadOnlyList<string> ElementKeysFor(
        ImageStepBlueprint blueprint,
        IReadOnlyList<ReferenceApplicationSelection> bindings)
        => StepPromptElementPlan.Build(BoundSlotsFor(blueprint, bindings).Select(slot => (slot.SlotKind, slot.ActorKey)));

    /// <summary>
    /// The slots that are actually bound to an image, in blueprint order, de-duplicated by slot.
    /// </summary>
    /// <remarks>
    /// Two renderers need two different names for the same fact, and this is where the fact lives.
    /// <list type="bullet">
    /// <item><description>
    /// The COMPILED-BRIEF path needs the payload element KEY (<see cref="ElementKeysFor"/>), because that is what
    /// <c>ScenePromptOverridesApplier</c> addresses - and it THROWS on a key it does not know, which is why a face
    /// cannot be given an element key of its own without a new side-channel in the applier and in the compiler.
    /// </description></item>
    /// <item><description>
    /// The TEMPLATE path (the LoRA cell) needs the SLOT, because its prompt carries one placeholder per element and
    /// the slot is what names it. Going through element keys would COLLAPSE face and body onto the one
    /// <c>appearance</c> key, so the template could no longer tell which of the two a reference supplied.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static IReadOnlyList<ImageStepSlotBinding> BoundSlotsFor(
        ImageStepBlueprint blueprint,
        IReadOnlyList<ReferenceApplicationSelection> bindings)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(bindings);

        var bound = new List<ImageStepSlotBinding>();
        foreach (var slot in blueprint.Slots)
        {
            var binding = FindBinding(bindings, slot.SlotKind, slot.ActorKey);
            if (binding is null || !binding.SuppliesImage)
            {
                continue;
            }

            bound.Add(new ImageStepSlotBinding(slot.SlotKind, slot.ActorKey));
        }

        return bound;
    }

    /// <summary>The operator-facing name of one slot. The template path's removal notice reads this.</summary>
    public static string SlotLabel(ImageStepSlotKind slotKind) => slotKind switch
    {
        ImageStepSlotKind.Face => "Face",
        ImageStepSlotKind.Body => "Body",
        ImageStepSlotKind.Wardrobe => "Clothing",
        ImageStepSlotKind.Location => "Location",
        ImageStepSlotKind.Pose => "Pose",
        ImageStepSlotKind.CharacterPose => "Pose and appearance",
        _ => slotKind.ToString()
    };

    /// <summary>
    /// <see cref="ElementKeysFor"/> as the operator reads it: the payload element's own name rather than its scope
    /// path, because "Appearance" is a fact about the prompt and <c>character:becky.appearance</c> is a fact about
    /// the plumbing.
    /// </summary>
    public static IReadOnlyList<string> ElementLabelsFor(
        ImageStepBlueprint blueprint,
        IReadOnlyList<ReferenceApplicationSelection> bindings)
        => ElementKeysFor(blueprint, bindings).Select(Label).ToList();

    /// <summary>The operator-facing name of one payload element key.</summary>
    public static string Label(string elementKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementKey);

        var element = elementKey[(elementKey.LastIndexOf('.') + 1)..];
        return element switch
        {
            "appearance" => "Appearance",
            "clothing" => "Clothing",
            "position" => "Position",
            "visibleAction" => "Visible action",
            "location" => "Location",
            "environment" => "Environment",
            _ => element
        };
    }

    /// <summary>
    /// The binding for one slot. Matched on kind AND actor, exactly as the composer matches it, so the omission set
    /// and the slot list can never be looking at two different bindings.
    /// </summary>
    private static ReferenceApplicationSelection? FindBinding(
        IReadOnlyList<ReferenceApplicationSelection> bindings,
        ImageStepSlotKind slotKind,
        string? actorKey)
        => bindings.FirstOrDefault(binding =>
            string.Equals(binding.Kind, slotKind.ToString(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(binding.ActorKey ?? string.Empty, actorKey ?? string.Empty, StringComparison.Ordinal));
}

/// <summary>One slot that is bound to a reference image, and the character it belongs to where it has one.</summary>
public sealed record ImageStepSlotBinding(ImageStepSlotKind SlotKind, string? ActorKey);
