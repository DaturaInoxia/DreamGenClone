namespace DreamGenClone.Components.Shared;

/// <summary>
/// An image the operator chose from INSIDE the app, shaped so a host can hand it to the same upload path it already
/// uses for a file off disk.
///
/// <para>
/// The point is that the bytes and the provenance travel together. A reference image that already exists in the app —
/// a face on a superseded pack, an approved front in the asset library — is the natural source for a new pack slot,
/// and an operator who has to find that file on disk and re-upload it is being asked to do the app's bookkeeping for
/// it. What they picked is also what the record should SAY they picked, which is why
/// <see cref="SourceLabel"/> is produced here rather than guessed by each host.
/// </para>
/// </summary>
/// <param name="SourceLabel">Human-readable provenance for the picked image, e.g. "identity pack v9 · Profile Left".</param>
/// <param name="FileName">A file name derived from where the image came from, for the target store's own naming.</param>
/// <param name="MediaType">The source's recorded media type.</param>
/// <param name="Content">The image's bytes.</param>
/// <param name="Identity">
/// Which stored image was picked, when the pick came from the asset library. A host that BINDS the image (rather than
/// uploading its bytes) needs the ids and the checksum the binding revalidates; a host that only uploads ignores this.
/// Null for an identity-pack pick, which is not a library asset.
/// </param>
public sealed record InAppPickedImage(
    string SourceLabel,
    string FileName,
    string MediaType,
    byte[] Content,
    InAppPickedImageIdentity? Identity = null,

    /// <summary>
    /// The app's own rendered image this pick came from, when it came from the production source. Null for every other
    /// source, which is a real answer: a host that binds BY REFERENCE must have this, and one that uploads bytes does
    /// not.
    /// </summary>
    InAppPickedRenderedImage? RenderedImage = null);

/// <summary>
/// The stored image a pick came from, carried so a host can bind it by reference instead of re-uploading its bytes.
///
/// <para>
/// The fields mirror what a reference binding has to record to survive a render revalidation — the image id, its
/// checksum and the production version — because a bare id is not enough to prove the render used the bytes that were
/// approved.
/// </para>
/// </summary>
public sealed record InAppPickedImageIdentity(
    string AssetId,
    string ImageId,
    string DisplayName,
    string? Sha256,
    int? ProductionVersion,
    string? FileRelativePath);
