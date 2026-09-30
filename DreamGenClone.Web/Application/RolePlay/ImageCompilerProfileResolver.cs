using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Resolves the compiler profile for a checkpoint (B-135 B135-003).
///
/// <para>
/// This is the single entry point for "what are this checkpoint's prompt rules". Resolution is keyed on the
/// checkpoint the render actually resolved to (<c>ResolvedImageModel.ModelIdentifier</c>), NOT on the model family:
/// the family-keyed registry cannot distinguish BigLust from Juggernaut (both SDXL) and silently served Qwen-2.1 and
/// FLUX the SDXL-branded builder.
/// </para>
///
/// <para>
/// A checkpoint with no profile is refused by name. There is deliberately no family-level default to fall back to:
/// a fallback would compile a prompt under rules the checkpoint does not follow, and the resulting image would look
/// exactly like one that honoured a researched profile — the failure mode governance rule 4 exists to prevent
/// ("a model with no researched settings cannot be the target of a compiler").
/// </para>
/// </summary>
public interface IImageCompilerProfileResolver
{
    /// <summary>The profile for the checkpoint this model resolved to. Throws when none is configured.</summary>
    Task<ImageCompilerProfile> ResolveAsync(ResolvedImageModel model, CancellationToken cancellationToken = default);

    /// <summary>The profile for a checkpoint identifier, by name. Throws when none is configured.</summary>
    Task<ImageCompilerProfile> ResolveAsync(
        string checkpointIdentifier,
        string context = "a direct checkpoint lookup",
        CancellationToken cancellationToken = default);
}

public sealed class ImageCompilerProfileResolver : IImageCompilerProfileResolver
{
    private readonly IImageCompilerProfileRepository _profiles;

    public ImageCompilerProfileResolver(IImageCompilerProfileRepository profiles)
    {
        _profiles = profiles;
    }

    public Task<ImageCompilerProfile> ResolveAsync(ResolvedImageModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var checkpoint = model.ModelIdentifier?.Trim() ?? string.Empty;
        if (checkpoint.Length == 0)
        {
            throw new InvalidOperationException(
                $"The resolved image model from provider '{model.ProviderName}' has no checkpoint identifier, so no "
                + "compiler profile can be resolved. Set the model's identifier in Model Manager.");
        }

        return ResolveAsync(checkpoint, $"model '{model.ProviderName}'", cancellationToken);
    }

    public async Task<ImageCompilerProfile> ResolveAsync(
        string checkpointIdentifier,
        string context = "a direct checkpoint lookup",
        CancellationToken cancellationToken = default)
    {
        var checkpoint = checkpointIdentifier?.Trim() ?? string.Empty;
        if (checkpoint.Length == 0)
        {
            throw new InvalidOperationException("A checkpoint identifier is required to resolve an image compiler profile.");
        }

        var profile = await _profiles.FindByCheckpointAsync(checkpoint, cancellationToken);
        if (profile is null)
        {
            throw new InvalidOperationException(
                $"No image compiler profile is configured for checkpoint '{checkpoint}' ({context}). Add a profile for "
                + "this checkpoint in the Playground before compiling or rendering a prompt with it: a compiler never "
                + "falls back to a family default, because a prompt compiled under another checkpoint's rules is "
                + "indistinguishable from a correct one.");
        }

        return profile;
    }
}
