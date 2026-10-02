using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

/// <summary>Runs a configured source-image edit and returns the rendered image bytes.</summary>
/// <remarks>
/// <paramref name="mask"/> is a NAMED opt-in and sits after the cancellation token on purpose: every existing call
/// site passes that token positionally, and a region is something a caller asks for by name rather than a value that
/// silently shifts into the token's place.
/// </remarks>
public interface IImageEditingClient
{
    Task<byte[]> EditAsync(
        ResolvedImageEditorModel model,
        Stream sourceImage,
        string sourceFileName,
        string instruction,
        CancellationToken cancellationToken = default,
        ImageEditingMask? mask = null);

    Task<byte[]> EditWithReferencesAsync(
        ResolvedImageEditorModel model,
        Stream sourceImage,
        string sourceFileName,
        string instruction,
        IReadOnlyList<ImageEditingReference> references,
        CancellationToken cancellationToken = default,
        ImageEditingMask? mask = null);
}

public sealed record ImageEditingReference(
    int Ordinal,
    string SemanticRole,
    Stream Image,
    string FileName,
    string Checksum);

/// <summary>
/// A REGION the edit is confined to (CASE-21). White pixels are the region the edit may change; black pixels are the
/// region that must survive.
///
/// A mask is not a hint: it changes the sampler's STARTING LATENT to <c>VAEEncodeForInpaint(source, mask)</c> instead of
/// the text encoder's own latent. The encoder's latent carries the source only as a REFERENCE, so an unmasked edit
/// regenerates the whole frame - which is why naming a region in the instruction cannot contain an edit, however
/// clearly it is named (measured: CASE-23). Selecting a region and containing it are different mechanisms.
/// </summary>
/// <param name="GrowMaskBy">Pixels the mask is grown by when it is BUILT (baked into the mask). A bare rectangle
/// edge leaves a visible seam (CASE-21), so this is an operator-set value and never invented here.</param>
/// <param name="FeatherPixels">Pixels of softening baked into the mask edge when it is built; 0 means a hard edge.
/// The softening is a blur of the mask itself, not the host FeatherMask node, which feathers only the frame border.</param>
/// <remarks>
/// The record OWNS <see cref="Mask"/>: a mask is built for one run, and whoever puts a stream in here hands over the
/// job of disposing it, so a caller must not pass a stream it still needs.
/// </remarks>
public sealed record ImageEditingMask(
    Stream Mask,
    string FileName,
    string Checksum,
    int GrowMaskBy,
    int FeatherPixels) : IDisposable
{
    public void Dispose() => Mask.Dispose();
}