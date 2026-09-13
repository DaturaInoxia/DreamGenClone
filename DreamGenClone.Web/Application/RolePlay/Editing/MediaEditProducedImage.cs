using SixLabors.ImageSharp;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// What a produced image's bytes say about themselves.
///
/// A record must describe the file that exists, not what was requested: a crop trims and an enhance
/// resamples, so the finished size is only known once the operation has run and must never be inherited
/// from the source image. The asset storage path already records dimensions at ingest; this is the same
/// fact read from the bytes the media-edit pipeline has produced.
/// </summary>
internal static class MediaEditProducedImage
{
    private const string NotIdentifiable =
        "The produced image bytes could not be identified, so their size cannot be recorded.";

    /// <summary>
    /// The produced bytes' dimensions as "width x height". Throws when the bytes are not an identifiable
    /// image: something wrote a file it cannot describe, and a guessed size would be worse than a failure.
    /// The decoder's own exception type is not part of this contract, so it is not what callers see.
    /// </summary>
    public static string SizeOf(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        ImageInfo? info;
        try
        {
            info = Image.Identify(bytes);
        }
        catch (UnknownImageFormatException ex)
        {
            throw new InvalidOperationException(NotIdentifiable, ex);
        }

        return info is null
            ? throw new InvalidOperationException(NotIdentifiable)
            : $"{info.Width}x{info.Height}";
    }
}
