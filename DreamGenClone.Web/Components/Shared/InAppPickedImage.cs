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
public sealed record InAppPickedImage(
    string SourceLabel,
    string FileName,
    string MediaType,
    byte[] Content);
