using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The ONE place that says which reference strategies are MEANINGFUL for each reference element, and the strategies
/// each image-step slot kind accepts.
///
/// This exists because the same catalogue had been written three times and had drifted: the apply panel's
/// <c>Location</c> arm listed only <c>ControlNet</c> (so a model that carries a location as its own reference image
/// could never offer it — B-128 §5.3 U1), the step composer kept a private switch whose default arm silently handed
/// <c>Location</c> and <c>Wardrobe</c> the face set, and the edit surface hardcoded
/// <c>["TextOnly","NativeMultiReference"]</c> as if that were the whole world.
///
/// Meaning lives here exactly once. Capability does NOT live here: it is the selected model's
/// <c>QualifiedStrategies</c>, and callers intersect the two through <see cref="Intersect"/>. Keeping them apart is
/// what stops a catalogue entry from ever offering a strategy no model can execute.
/// </summary>
public static class ReferenceStrategyCatalogue
{
    public const string TextOnly = "TextOnly";
    public const string ReferenceConditioning = "ReferenceConditioning";
    public const string NativeMultiReference = "NativeMultiReference";
    public const string ControlNet = "ControlNet";
    public const string Lora = "Lora";
    public const string WardrobeTryOn = "WardrobeTryOn";

    /// <summary>
    /// A surface that renders an image from a prompt and a set of references. It exists so "which strategies does
    /// this code path implement?" is ANSWERED here rather than spelled as a string literal at each call site, which
    /// is how one surface ended up asking for a strategy no graph of its owns.
    /// </summary>
    public enum ReferenceImageSurface
    {
        /// <summary>Prompt-to-image: the scene render and the asset generation.</summary>
        Generate = 1,

        /// <summary>Source-image editing: the media-edit pipeline and the asset edit handler.</summary>
        Edit = 2
    }

    /// <summary>
    /// The strategies an image surface actually IMPLEMENTS — every one of them carried as a reference IMAGE.
    ///
    /// <para>
    /// <b>Generate</b> carries references through <c>ComfyUIImageClient.BuildQwenImage21Workflow</c> (reference *i*
    /// wired to <c>TextEncodeQwenImage21.images.image_{i+1}</c>). <b>Edit</b> carries them through
    /// <c>ComfyUIImageEditingClient</c>, whose every graph kind (split-UNET, merged checkpoint, Qwen-2.1 native)
    /// accepts reference images — the source occupies <c>image_1</c> and reference *i* occupies
    /// <c>image_{i+2}</c>.
    /// </para>
    ///
    /// <para>
    /// <c>ReferenceConditioning</c> is deliberately NOT in the edit set: that is the IP-Adapter/PuLID identity
    /// MECHANISM, which conditions the sampler's model input, and no editor graph implements it — the identity
    /// mechanism is a scene-render concept (<c>IIdentityConditionedImageClient</c>). Demanding it on an edit is what
    /// failed every reference-carrying asset edit with "qualified but has no implemented graph in this editor"
    /// (reported live 2026-10-03), because the editor model declares only <c>NativeMultiReference</c>.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> ImplementedReferenceStrategiesFor(ReferenceImageSurface surface) => surface switch
    {
        ReferenceImageSurface.Generate => [NativeMultiReference],
        ReferenceImageSurface.Edit => [NativeMultiReference],
        _ => throw new InvalidOperationException(
            $"Reference image surface '{surface}' has no implemented-strategy set, so no reference can be proven "
            + "carryable on it. Add it deliberately rather than letting a caller assume one.")
    };

    /// <summary>The surface's own name, for a refusal that says which code path refused.</summary>
    public static string Label(ReferenceImageSurface surface) => surface switch
    {
        ReferenceImageSurface.Generate => "generation",
        ReferenceImageSurface.Edit => "editor",
        _ => surface.ToString()
    };

    private static readonly IReadOnlyList<string> FaceStrategies =
        [TextOnly, ReferenceConditioning, NativeMultiReference, Lora];

    private static readonly IReadOnlyList<string> BodyStrategies =
        [TextOnly, Lora, NativeMultiReference];

    private static readonly IReadOnlyList<string> WardrobeStrategies =
        [TextOnly, WardrobeTryOn, NativeMultiReference];

    private static readonly IReadOnlyList<string> LocationStrategies =
        [TextOnly, NativeMultiReference, ControlNet];

    private static readonly IReadOnlyList<string> PoseStrategies =
        [TextOnly, NativeMultiReference, ControlNet];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ByElementKey =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Identity"] = FaceStrategies,
            ["Face"] = FaceStrategies,
            ["Body"] = BodyStrategies,
            ["Wardrobe"] = WardrobeStrategies,
            ["Location"] = LocationStrategies,
            ["Pose"] = PoseStrategies,
            ["CharacterPose"] = PoseStrategies
        };

    /// <summary>
    /// The strategies an element key accepts, when the key is known. An unknown key is the caller's to report: the
    /// panel logs it and degrades to text, and a silent catalogue miss here would hide an unreachable strategy.
    /// </summary>
    public static bool TryForElementKey(string? elementKey, out IReadOnlyList<string> strategies) =>
        ByElementKey.TryGetValue(elementKey ?? string.Empty, out strategies!);

    /// <summary>
    /// The strategies a step slot accepts. Keyed by the slot's element key, so the step composer and the apply panel
    /// cannot drift apart again — they read the same entries.
    /// </summary>
    public static IReadOnlyList<string> ForSlotKind(ImageStepSlotKind slotKind) =>
        ByElementKey[ReferenceSlotPlanner.ElementKeyFor(slotKind)];
    /// <summary>
    /// The element key a slot kind reports as. Exposed here so a host that seeds a binding and the composer that
    /// renders the slot name it identically instead of each spelling it out.
    /// </summary>
    public static string ElementKeyForSlot(ImageStepSlotKind slotKind) =>
        ReferenceSlotPlanner.ElementKeyFor(slotKind);
    /// <summary>
    /// Meaning intersected with what the selected model can execute, preserving the ORDER of the meaning list so the
    /// operator sees a stable menu. A model that qualifies for nothing yields an empty list, never a guessed set.
    /// </summary>
    public static IReadOnlyList<string> Intersect(
        IReadOnlyList<string> meaningfulStrategies,
        IReadOnlyList<string> executableStrategies)
    {
        ArgumentNullException.ThrowIfNull(meaningfulStrategies);
        ArgumentNullException.ThrowIfNull(executableStrategies);

        var executable = new HashSet<string>(executableStrategies, StringComparer.OrdinalIgnoreCase);
        return meaningfulStrategies.Where(executable.Contains).ToArray();
    }
}
