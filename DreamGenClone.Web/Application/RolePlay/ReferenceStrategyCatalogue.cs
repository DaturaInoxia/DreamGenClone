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
