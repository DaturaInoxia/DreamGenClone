namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Which view a body view comes FROM — the derivation chain, in the one place that answers it.
///
/// <para>
/// It answers TWO questions, and they are deliberately not the same question:
/// </para>
///
/// <list type="bullet">
/// <item><see cref="RenderBaseFor"/> — the image an ANGLE is RENDERED from. This is a structural conditioning: the
/// model is shown that image and asked to turn it, so the choice decides what the render can possibly know.</item>
/// <item><see cref="EditSourceFor"/> — the image a view is EDITED from ("Rotation edit" / "Edit from accepted
/// source"). Here the operator picks the source, and the chain says which one the surface offers by default.</item>
/// </list>
///
/// <para>
/// They used to live apart — the render gate hardcoded the FRONT for every angle while the edit path already derived
/// a profile from the same-side three-quarter — so the code contained two answers to one question and neither surface
/// could tell. Keeping both here, adjacent, is what makes their one deliberate difference visible instead of
/// mysterious. <c>BodyViewDerivationTests</c> pins that difference.
/// </para>
///
/// <para>
/// The clothed front is the root: it is generated or uploaded, so nothing derives it. Everything else is the same
/// body seen another way.
/// </para>
/// </summary>
public static class BodyViewDerivation
{
    /// <summary>
    /// The image this view is RENDERED from, or null when it is a base that is generated rather than derived.
    ///
    /// <para>
    /// The rule, and why it is not uniform: the three-quarters and the back are one turn away from the front, and the
    /// front plus their skeleton was measured to produce them correctly (2026-09-23, <c>body-angle-*</c> cases). A
    /// PROFILE of an UNCLOTHED body is different: it is rendered from the same-side three-quarter, so the render is
    /// shown the body already turned three-quarters of the way rather than asked to reach edge-on in one step
    /// (operator decision, 2026-10-10). A CLOTHED profile still renders from the front — the clothed set was measured
    /// working that way and is left alone.
    /// </para>
    ///
    /// <para>
    /// Neither route falls back: a source that has not been accepted is a refusal, because a view that quietly
    /// renders from something else is a different body than the operator asked for.
    /// </para>
    /// </summary>
    public static CharacterIdentityBodyViewKey? RenderBaseFor(CharacterIdentityBodyViewKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return key.View switch
        {
            // A base is generated or uploaded; it has no render source.
            SceneImageReferenceBodyView.Front => null,

            SceneImageReferenceBodyView.ProfileLeft when key.State == SceneImageReferenceBodyState.Unclothed =>
                CharacterIdentityBodyViewKey.Canonical(key.State, SceneImageReferenceBodyView.ThreeQuarterLeft),
            SceneImageReferenceBodyView.ProfileRight when key.State == SceneImageReferenceBodyState.Unclothed =>
                CharacterIdentityBodyViewKey.Canonical(key.State, SceneImageReferenceBodyView.ThreeQuarterRight),

            // The three-quarters, the clothed profiles, the back and every extended rotation come off the front.
            _ => CharacterIdentityBodyViewKey.Canonical(key.State, SceneImageReferenceBodyView.Front)
        };
    }

    /// <summary>
    /// The view a request is EDITED from, or null when it is a base. The clothed front is the root — it is generated
    /// or uploaded. The unclothed front is the same body without clothing, so it is an edit of that accepted base;
    /// the three-quarters (and every extended rotation) come off the front base, and a profile off the three-quarter
    /// on its own side — the same chain the face angles use.
    /// </summary>
    public static CharacterIdentityBodyViewKey? EditSourceFor(CharacterIdentityBodyViewKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return key.View switch
        {
            SceneImageReferenceBodyView.Front when key.State == SceneImageReferenceBodyState.Unclothed =>
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
            SceneImageReferenceBodyView.Front => null,

            // For a CLOTHED profile this is the one cell where the edit chain and the render base disagree: the edit
            // is a rotation of the same-side three-quarter, while the render is a turn from the front.
            SceneImageReferenceBodyView.ProfileLeft => CharacterIdentityBodyViewKey.Canonical(
                key.State, SceneImageReferenceBodyView.ThreeQuarterLeft),
            SceneImageReferenceBodyView.ProfileRight => CharacterIdentityBodyViewKey.Canonical(
                key.State, SceneImageReferenceBodyView.ThreeQuarterRight),
            _ => CharacterIdentityBodyViewKey.Canonical(key.State, SceneImageReferenceBodyView.Front)
        };
    }
}
