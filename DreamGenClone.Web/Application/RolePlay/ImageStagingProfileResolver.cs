using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Resolves the staging profile that governs a render surface (B-157).
///
/// <para>
/// There is exactly ONE resolution path, and it has no in-code fallback: a surface with no configured row is refused
/// by name. A stand-in profile would place subjects the operator never configured, and the resulting image is
/// indistinguishable from one that honoured a real choice.
/// </para>
/// </summary>
public interface IImageStagingProfileResolver
{
    /// <summary>The configured profile for a surface, or a fail-fast naming the surface and where to configure it.</summary>
    Task<ImageStagingProfile> ResolveAsync(ImageStagingSurface surface, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ImageStagingProfileResolver : IImageStagingProfileResolver
{
    private readonly IImageStagingProfileRepository _repository;

    public ImageStagingProfileResolver(IImageStagingProfileRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ImageStagingProfile> ResolveAsync(ImageStagingSurface surface, CancellationToken cancellationToken = default)
    {
        if (surface == ImageStagingSurface.Unknown)
        {
            throw new InvalidOperationException(
                "An image staging profile cannot be resolved for the 'Unknown' surface. Name the surface the render "
                + "belongs to (Composition or AssetManager).");
        }

        var profile = await _repository.FindBySurfaceAsync(surface, cancellationToken);
        if (profile is null)
        {
            throw new InvalidOperationException(
                $"No image staging profile is configured for the '{surface}' surface, so the render's subjects cannot "
                + "be placed in the frame. Configure it on /profiles (Image Staging), and do not expect a default: "
                + "placing subjects the operator never chose would produce an image that looks honoured.");
        }

        return profile;
    }
}
