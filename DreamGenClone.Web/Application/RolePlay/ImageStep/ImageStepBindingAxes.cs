using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Turns a step's bound references into the DECLARED BINDING AXES a compiler reads.
///
/// <para>
/// This is the translation between the two binding vocabularies, and it had to exist: the compiler decides which
/// elements the prompt must NOT describe from the axes that something structural already carries (D4), so a compile
/// performed with an empty axis list produces a prompt that describes the face, the build and the pose that the
/// reference images are also supplying — two sources for one element, which is the defect the binding contract exists
/// to prevent. The asset creator bound a face and a build and could not compile at all; the correct answer was to
/// translate them, never to refuse.
/// </para>
///
/// <para>
/// <b>Why face and build MERGE into one axis.</b> <see cref="ImageBindingAxis"/> names which part of the frame a binding
/// carries, and it has no Body member: a character's face and their build are both "who this person looks like", which
/// is <see cref="ImageBindingAxis.Identity"/>. The axis vocabulary also allows exactly ONE entry per axis
/// (<see cref="ImageCellBindings.Validate"/> refuses a second, because two entries would leave the axis's mechanism
/// ambiguous), so the two references are declared as one Identity axis rather than two. What the render does with
/// each image is unchanged — both still travel as their own reference; this only states which axis the PROMPT leaves
/// alone.
/// </para>
///
/// <para>
/// Pure and static on purpose, like <see cref="ReferenceSlotPlanner"/>: the mapping decides what a compiled prompt is
/// allowed to say, so it can be read and proved without a database, a model or a render.
/// </para>
/// </summary>
public static class ImageStepBindingAxes
{
    /// <summary>
    /// The axes these references carry, in the step's own order.
    ///
    /// Text-only bindings ARE reported, as <see cref="ImageBindingMode.Text"/>: an element the operator deliberately
    /// left to the prose is a fact the compiler should have, because it is the difference between "nothing is
    /// declared" and "this element is the text's job".
    /// </summary>
    public static IReadOnlyList<ImageCellBinding> ToCellBindings(
        IReadOnlyList<ReferenceApplicationSelection> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var ordered = bindings
            .OrderBy(binding => binding.Ordinal ?? int.MaxValue)
            .ToList();

        var byAxis = new List<(ImageBindingAxis Axis, ImageCellBinding Binding)>();
        foreach (var binding in ordered)
        {
            var candidate = ForOne(binding);
            var index = byAxis.FindIndex(entry => entry.Axis == candidate.Axis);
            if (index < 0)
            {
                byAxis.Add((candidate.Axis, candidate));
                continue;
            }

            // One entry per axis. The STRUCTURAL carrier wins a disagreement: an axis a reference image supplies must
            // not be described in words as well, so a text-only sibling on the same axis is absorbed rather than
            // allowed to switch the axis back to prose.
            var existing = byAxis[index].Binding;
            byAxis[index] = (candidate.Axis, IsStructural(existing) ? existing : candidate);
        }

        var declared = byAxis.Select(entry => entry.Binding).ToList();
        if (declared.Count > 0)
        {
            // One definition of a valid declaration, applied here rather than trusted: a step that produced an
            // impossible axis set must fail at the translation, not inside a render three steps later.
            ImageCellBindings.Validate(declared);
        }

        return declared;
    }

    /// <summary>
    /// The axis ONE bound reference declares, with the mode and value its own strategy implies.
    /// </summary>
    private static ImageCellBinding ForOne(ReferenceApplicationSelection binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var axis = AxisFor(binding);
        var strategy = binding.Strategy?.Trim() ?? string.Empty;
        if (strategy.Length == 0)
        {
            throw new InvalidOperationException(
                $"The {binding.Kind ?? binding.ElementKey} reference carries no strategy, so there is no way to say how "
                + "it is carried. A bound reference always declares one - an unqualified strategy fails at render time "
                + "rather than being silently downgraded.");
        }

        if (string.Equals(strategy, ReferenceStrategyCatalogue.TextOnly, StringComparison.OrdinalIgnoreCase))
        {
            // Text carries the axis in the prompt. The value, strength and strategy are deliberately dropped: a Text
            // binding that names a source would be a second, uncompared source for the same element.
            return new ImageCellBinding(axis, ImageBindingMode.Text);
        }

        if (IsReferenceStrategy(strategy))
        {
            return new ImageCellBinding(axis, ImageBindingMode.Reference, ValueFor(binding, strategy), Strategy: strategy);
        }

        if (IsAdapterStrategy(strategy))
        {
            return new ImageCellBinding(
                axis, ImageBindingMode.Adapter, ValueFor(binding, strategy), Strategy: AdapterFor(binding, strategy));
        }

        if (string.Equals(strategy, ReferenceStrategyCatalogue.Lora, StringComparison.OrdinalIgnoreCase))
        {
            // A LoRA is a trained ARTIFACT, not an image, and the chain the render applies comes from the LoRA selection
            // that rides with the request - not from a step binding. Declaring the axis here would name an artifact this
            // reference does not carry, and the compiler would then leave out an element nothing had supplied.
            throw new InvalidOperationException(
                $"The {binding.Kind ?? binding.ElementKey} reference is bound through '{ReferenceStrategyCatalogue.Lora}', "
                + "which carries the axis as a trained artifact rather than as an image. Choose the LoRA in the render's "
                + "own LoRA selection, or bind this element as a reference image.");
        }

        throw new InvalidOperationException(
            $"The {binding.Kind ?? binding.ElementKey} reference is bound through strategy '{strategy}', which this "
            + "translation does not know how to declare. Known reference strategies: "
            + $"{ReferenceStrategyCatalogue.NativeMultiReference}, {ReferenceStrategyCatalogue.ReferenceConditioning}, "
            + $"{ReferenceStrategyCatalogue.WardrobeTryOn}, {ReferenceStrategyCatalogue.TextOnly}. Known adapter "
            + $"strategies: {ReferenceStrategyCatalogue.ControlNet}, {ReferenceStrategyResolver.PoseControlNet}. Add the "
            + "strategy here with the mechanism it means, rather than compiling with an axis set that does not include "
            + "this reference.");
    }

    /// <summary>
    /// Which part of the frame a slot carries. Read from the binding's own <c>Kind</c> (an
    /// <see cref="ImageStepSlotKind"/> name), falling back to the legacy element key for a selection persisted before
    /// slots existed.
    /// </summary>
    private static ImageBindingAxis AxisFor(ReferenceApplicationSelection binding)
    {
        var slotKind = SlotKindFor(binding);

        return slotKind switch
        {
            ImageStepSlotKind.Face => ImageBindingAxis.Identity,
            // Merged with the face: the axis vocabulary has no Body, and a build is part of who the person looks like.
            ImageStepSlotKind.Body => ImageBindingAxis.Identity,
            ImageStepSlotKind.Wardrobe => ImageBindingAxis.Wardrobe,
            ImageStepSlotKind.Location => ImageBindingAxis.Location,
            ImageStepSlotKind.Pose => ImageBindingAxis.Pose,
            // A character pose asset carries appearance, clothing AND position; the axe the reference PINS is the body
            // position the frame is built around.
            ImageStepSlotKind.CharacterPose => ImageBindingAxis.Pose,
            _ => throw new InvalidOperationException(
                $"The reference '{binding.Kind ?? binding.ElementKey}' names no reference slot, so the axis it carries "
                + "cannot be declared. Known slots: " + string.Join(", ", Enum.GetNames<ImageStepSlotKind>()) + ".")
        };
    }

    private static ImageStepSlotKind SlotKindFor(ReferenceApplicationSelection binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.Kind)
            && Enum.TryParse<ImageStepSlotKind>(binding.Kind.Trim(), ignoreCase: true, out var fromKind))
        {
            return fromKind;
        }

        // Legacy selections carry only the element key, which is how a slot has always reported itself.
        return (binding.ElementKey ?? string.Empty).Trim() switch
        {
            "Identity" => ImageStepSlotKind.Face,
            "Body" => ImageStepSlotKind.Body,
            "Wardrobe" => ImageStepSlotKind.Wardrobe,
            "Location" => ImageStepSlotKind.Location,
            "Pose" => ImageStepSlotKind.Pose,
            "CharacterPose" => ImageStepSlotKind.CharacterPose,
            var key => throw new InvalidOperationException(
                $"The reference element '{key}' is not a reference slot this app declares, so the axis it carries "
                + "cannot be declared.")
        };
    }

    /// <summary>What the binding names, for the compiler's own statement of what is carried.</summary>
    private static string ValueFor(ReferenceApplicationSelection binding, string strategy)
    {
        // A pack image is named by its PACK: a pack asset id is only meaningful inside its pack, which is why the two
        // travel together everywhere else too.
        if (!string.IsNullOrWhiteSpace(binding.IdentityPackId))
        {
            return binding.IdentityPackId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(binding.SceneAssetId))
        {
            return binding.SceneAssetId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(binding.PosePresetId))
        {
            return binding.PosePresetId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(binding.SkeletonRelativePath))
        {
            return binding.SkeletonRelativePath.Trim();
        }

        throw new InvalidOperationException(
            $"The {binding.Kind ?? binding.ElementKey} reference is bound through '{strategy}' but names nothing: no "
            + "identity pack, no scene asset and no pose preset. A structural binding with no source would produce an "
            + "image the prompt has been told not to describe.");
    }

    /// <summary>
    /// The control adapter a graph strategy actually carries. A pose skeleton IS an OpenPose map, which is why that
    /// one can be named; anything else would be a guess, and a guessed adapter is a graph the operator did not choose.
    /// </summary>
    private static string AdapterFor(ReferenceApplicationSelection binding, string strategy)
    {
        var isPoseSkeleton = string.Equals(strategy, ReferenceStrategyResolver.PoseControlNet, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(binding.SkeletonRelativePath)
            || !string.IsNullOrWhiteSpace(binding.PosePresetId);

        if (isPoseSkeleton)
        {
            return "OpenPose";
        }

        throw new InvalidOperationException(
            $"The {binding.Kind ?? binding.ElementKey} reference is bound through '{strategy}', and the binding does not "
            + "say which control map it carries (OpenPose / Depth / Canny). Add the adapter to the binding, or bind this "
            + "element as a reference image instead.");
    }

    private static bool IsReferenceStrategy(string strategy) =>
        string.Equals(strategy, ReferenceStrategyCatalogue.NativeMultiReference, StringComparison.OrdinalIgnoreCase)
        || string.Equals(strategy, ReferenceStrategyCatalogue.ReferenceConditioning, StringComparison.OrdinalIgnoreCase)
        || string.Equals(strategy, ReferenceStrategyCatalogue.WardrobeTryOn, StringComparison.OrdinalIgnoreCase);

    private static bool IsAdapterStrategy(string strategy) =>
        string.Equals(strategy, ReferenceStrategyCatalogue.ControlNet, StringComparison.OrdinalIgnoreCase)
        || string.Equals(strategy, ReferenceStrategyResolver.PoseControlNet, StringComparison.OrdinalIgnoreCase);

    private static bool IsStructural(ImageCellBinding binding) => binding.Mode != ImageBindingMode.Text;
}
