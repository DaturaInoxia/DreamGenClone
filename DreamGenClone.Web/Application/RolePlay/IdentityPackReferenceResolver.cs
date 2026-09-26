using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Picks the reference image out of an identity pack for a face or body slot.
///
/// <para>
/// This is THE decision that makes a render the character, so it is pure and static: it can be proved without a
/// database, and every caller - the LoRA cell's render, the step pre-fill, and any future pack-backed surface - reads
/// the same one. Two matchers, because the two axes are different: a face is matched by the ANGLE the frame shows, a
/// body by the angle AND the wardrobe state, since a reference image carries its state with it (measured 2026-09-23: a
/// bare-shouldered reference made a clothed render come out unclothed).
/// </para>
///
/// <para>
/// Nothing is substituted. No match returns <c>null</c> and the CALLER decides how to refuse, because the wording of
/// that refusal belongs to the surface ("shoot the Faces tab first" is advice a render path can give and a picker
/// cannot). Silently falling back to another angle, or to the canonical view, would train a set on a face that does not
/// match the view it was learned from.
/// </para>
/// </summary>
public static class IdentityPackReferenceResolver
{
    /// <summary>The newest approved face reference for <paramref name="view"/>, or null when the pack has none.</summary>
    public static SceneImageReferenceAsset? ResolveFace(
        IReadOnlyList<SceneImageReferenceAsset> packAssets,
        SceneImageReferenceFaceView view)
    {
        ArgumentNullException.ThrowIfNull(packAssets);

        return NewestApproved(packAssets.Where(asset =>
            asset.AssetKind == SceneImageReferenceAssetKind.Face && asset.FaceView == view));
    }

    /// <summary>
    /// The newest approved body reference for <paramref name="view"/> in <paramref name="state"/>, or null when the
    /// pack has none. Matching on the view alone would hand a clothed render the unclothed reference.
    /// </summary>
    public static SceneImageReferenceAsset? ResolveBody(
        IReadOnlyList<SceneImageReferenceAsset> packAssets,
        SceneImageReferenceBodyView view,
        SceneImageReferenceBodyState state)
    {
        ArgumentNullException.ThrowIfNull(packAssets);

        return NewestApproved(packAssets
            .Where(asset => asset.AssetKind == SceneImageReferenceAssetKind.FullBody)
            .Where(asset => asset.BodyView == view && asset.BodyState == state));
    }

    /// <summary>
    /// Every approved face view the pack can serve, so a picker can offer only what exists instead of every slot the
    /// enum knows. Ordered by the enum's own order, which is the angle order the pack grid uses.
    /// </summary>
    public static IReadOnlyList<SceneImageReferenceFaceView> AvailableFaceViews(
        IReadOnlyList<SceneImageReferenceAsset> packAssets)
    {
        ArgumentNullException.ThrowIfNull(packAssets);

        return packAssets
            .Where(asset => asset.AssetKind == SceneImageReferenceAssetKind.Face && asset.IsApproved)
            .Select(asset => asset.FaceView)
            .Where(view => view is not null)
            .Select(view => view!.Value)
            .Distinct()
            .OrderBy(view => view)
            .ToList();
    }

    /// <summary>
    /// Every (body view, state) pair the pack can serve, for the same reason: a picker that offered a pair the pack
    /// cannot serve would be a control the operator can never satisfy.
    /// </summary>
    public static IReadOnlyList<(SceneImageReferenceBodyView View, SceneImageReferenceBodyState State)> AvailableBodies(
        IReadOnlyList<SceneImageReferenceAsset> packAssets)
    {
        ArgumentNullException.ThrowIfNull(packAssets);

        return packAssets
            .Where(asset => asset.AssetKind == SceneImageReferenceAssetKind.FullBody && asset.IsApproved)
            .Where(asset => asset.BodyView is not null && asset.BodyState is not null)
            .Select(asset => (View: asset.BodyView!.Value, State: asset.BodyState!.Value))
            .Distinct()
            .OrderBy(pair => pair.View)
            .ThenBy(pair => pair.State)
            .ToList();
    }

    private static SceneImageReferenceAsset? NewestApproved(IEnumerable<SceneImageReferenceAsset> matches) =>
        matches.Where(asset => asset.IsApproved)
            .OrderByDescending(asset => asset.CreatedUtc)
            .FirstOrDefault();
}
