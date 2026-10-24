namespace DreamGenClone.Components.Shared;

/// <summary>
/// The app's OWN rendered image a pick came from — a Studio production attempt, a gallery render, an edit — carried so
/// a host can bind it BY REFERENCE instead of re-uploading its bytes.
/// </summary>
/// <remarks>
/// A separate record from <see cref="InAppPickedImageIdentity"/>, and not a widening of it: that one addresses an
/// approved image in the asset store (an asset id, a production version), and a rendered image has neither. Folding
/// the two together would mean a consumer reading <c>AssetId</c> off a rendered image and finding an empty string,
/// which is exactly the kind of "which store am I in?" confusion the reference channels exist to prevent.
/// </remarks>
/// <param name="ImageId">The <c>SceneImageRecord</c> id — the id the reference channel resolves by.</param>
/// <param name="DisplayName">The operator's name for it, when it has one.</param>
/// <param name="Sha256">Its checksum, which is what makes a binding to it exact.</param>
public sealed record InAppPickedRenderedImage(
    string ImageId,
    string? DisplayName,
    string Sha256);
