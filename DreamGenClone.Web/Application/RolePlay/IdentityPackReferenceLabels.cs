using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The operator-facing names for the pack reference axes, in ONE place.
///
/// These strings were already duplicated as private helpers in two surfaces, and a third copy is how two pickers start
/// calling the same angle different things - the same drift the strategy catalogue was created to end. A missing view
/// falls back to the enum's own name rather than an empty label, because a blank option in a picker is unreadable.
/// </summary>
public static class IdentityPackReferenceLabels
{
    public static string FaceView(SceneImageReferenceFaceView view) => view switch
    {
        SceneImageReferenceFaceView.Front => "Front",
        SceneImageReferenceFaceView.ThreeQuarterLeft => "3/4 Left",
        SceneImageReferenceFaceView.ThreeQuarterRight => "3/4 Right",
        SceneImageReferenceFaceView.ProfileLeft => "Profile Left",
        SceneImageReferenceFaceView.ProfileRight => "Profile Right",
        _ => view.ToString()
    };

    public static string BodyView(SceneImageReferenceBodyView view) => view switch
    {
        SceneImageReferenceBodyView.Front => "Front",
        SceneImageReferenceBodyView.ThreeQuarterLeft => "3/4 Left",
        SceneImageReferenceBodyView.ThreeQuarterRight => "3/4 Right",
        SceneImageReferenceBodyView.ProfileLeft => "Profile Left",
        SceneImageReferenceBodyView.ProfileRight => "Profile Right",
        SceneImageReferenceBodyView.Back => "Back",
        _ => view.ToString()
    };

    public static string BodyState(SceneImageReferenceBodyState state) => state switch
    {
        SceneImageReferenceBodyState.Clothed => "Clothed",
        SceneImageReferenceBodyState.Unclothed => "Unclothed",
        _ => state.ToString()
    };

    /// <summary>
    /// One line naming an approved reference by the axis it is filed under, for a picker's option label. The state is
    /// part of a body reference's name on purpose: two options that read the same would hide which wardrobe state each
    /// one carries.
    /// </summary>
    public static string Describe(SceneImageReferenceAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        return asset.AssetKind switch
        {
            SceneImageReferenceAssetKind.Face when asset.FaceView is { } faceView => FaceView(faceView),
            SceneImageReferenceAssetKind.FullBody when asset.BodyView is { } bodyView && asset.BodyState is { } state
                => $"{BodyView(bodyView)} · {BodyState(state)}",
            _ => asset.AssetKind.ToString()
        };
    }
}
