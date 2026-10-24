namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Which element CLASSES the bound reference images of a <c>User Intent</c> draft actually supply.
/// </summary>
/// <remarks>
/// <para>
/// The action-only intent contract (see <see cref="SceneImageCompilerSystemPrompts.Qwen21CanonicalActionOnly"/>) was
/// written for a request whose references supplied the setting, the appearance, the clothing and the objects. It was
/// then selected for ANY reference-bound intent, and it asserted all four classes as supplied — so a render bound to
/// an identity plate and a location plate was still told "the clothing comes from a bound image… state nothing about
/// what is or is not worn". With no clothing image in the request, that rule suppressed the operator's own clothing
/// instruction: a measured case (SceneImage a597ba82, 2026-10-18) asked for "naked from the waist up" and no compiled
/// prompt could state it, so every render kept the garment on.
/// </para>
/// <para>
/// This record is what the contract is now driven by: a class that an image actually supplies is left out of the prose,
/// and a class that nothing supplies stays the operator's to state. It is derived from the same
/// <c>ReferenceBindingPromptRemoval</c> scopes the user-prompt ownership notice uses, so the two cannot disagree about
/// what the references own.
/// </para>
/// </remarks>
public readonly record struct IntentReferenceCoverage(
    bool Setting,
    bool Appearance,
    bool Clothing,
    bool Pose,
    bool Objects)
{
    /// <summary>Whether any reference contributes at all — the condition that selects the reference-aware contract.</summary>
    public bool Any => Setting || Appearance || Clothing || Pose || Objects;

    /// <summary>No reference supplies anything: the draft is compiled under the family's long-form contract.</summary>
    public static IntentReferenceCoverage None => default;
}
