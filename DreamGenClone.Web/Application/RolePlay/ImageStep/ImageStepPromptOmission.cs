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
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(bindings);

        var filled = new List<(ImageStepSlotKind SlotKind, string? ActorKey)>();
        foreach (var slot in blueprint.Slots)
        {
            var binding = FindBinding(bindings, slot.SlotKind, slot.ActorKey);
            if (binding is null || !binding.SuppliesImage)
            {
                continue;
            }

            filled.Add((slot.SlotKind, slot.ActorKey));
        }

        return StepPromptElementPlan.Build(filled);
    }

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
