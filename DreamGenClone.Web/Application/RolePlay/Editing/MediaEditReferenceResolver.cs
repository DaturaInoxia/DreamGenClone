using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Builds the reference set for a run. This was duplicated in the scene and asset editing handlers —
/// same 45-line loop twice, with only the qualified strategy name differing — so a fix landed twice
/// or, as happened, once.
/// </summary>
public sealed class MediaEditReferenceResolver
{
    private readonly ISceneAssetRepository _assets;
    private readonly ISceneAssetStorageService _storage;
    private readonly IReferenceStrategyResolver _strategies;

    public MediaEditReferenceResolver(
        ISceneAssetRepository assets,
        ISceneAssetStorageService storage,
        IReferenceStrategyResolver strategies)
    {
        _assets = assets;
        _storage = storage;
        _strategies = strategies;
    }

    /// <summary>
    /// Resolves every application that actually carries a reference, verifying the qualified strategy
    /// and the approved immutable asset selection. Returns an empty list when nothing references an
    /// image, which the caller turns into a plain text edit.
    /// </summary>
    public async Task<IReadOnlyList<MediaEditReference>> ResolveAsync(
        string? registeredModelId,
        IReadOnlyList<ReferenceApplicationSelection> applications,
        string qualifiedStrategy,
        CancellationToken cancellationToken = default)
    {
        var withReferences = applications
            .Where(application => application.UsesReference
                && !string.Equals(application.Strategy, "TextOnly", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (withReferences.Count == 0)
            return [];

        if (string.IsNullOrWhiteSpace(registeredModelId))
        {
            throw new InvalidOperationException(
                "Reference editing requires the exact registered editor model id.");
        }

        var references = new List<MediaEditReference>(withReferences.Count);
        for (var index = 0; index < withReferences.Count; index++)
        {
            var application = withReferences[index];
            var resolution = await _strategies.ResolveAsync(registeredModelId, application.Strategy, cancellationToken);
            if (!resolution.IsAvailable)
            {
                throw new InvalidOperationException(
                    $"Reference strategy '{application.Strategy}' for '{application.ElementKey}' is unavailable: {resolution.Reason}");
            }

            if (!string.Equals(resolution.Strategy, qualifiedStrategy, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Reference strategy '{resolution.Strategy}' for '{application.ElementKey}' is qualified but has no implemented graph in this editor.");
            }

            var assetImage = await _assets.GetImageAsync(application.SceneAssetImageId!, cancellationToken)
                ?? throw new InvalidOperationException($"Reference image '{application.SceneAssetImageId}' was not found.");
            if (!string.Equals(assetImage.AssetId, application.SceneAssetId, StringComparison.Ordinal)
                || assetImage.ProductionApprovalStatus != SceneAssetProductionApprovalStatus.Approved
                || assetImage.ProductionVersion != application.SceneAssetVersion
                || !string.Equals(assetImage.Sha256, application.SceneAssetSha256, StringComparison.Ordinal)
                || assetImage.Status != SceneAssetStatus.Complete
                || string.IsNullOrWhiteSpace(assetImage.FileRelativePath))
            {
                throw new InvalidOperationException(
                    $"Reference image '{application.SceneAssetImageId}' no longer matches its approved immutable selection.");
            }

            var relativePath = assetImage.FileRelativePath;
            references.Add(new MediaEditReference(
                index + 1,
                DescribeReference(application.SemanticRole, assetImage.DisplayName),
                $"{assetImage.Id}.png",
                assetImage.Sha256,
                token => _storage.OpenReadAsync(relativePath, token)));
        }

        return references;
    }

    /// <summary>
    /// What one reference IS, for the render's own provenance record — "location continuity (Left side)".
    ///
    /// <para>
    /// The name is read from the RESOLVED image row rather than from the binding's snapshot of it, because this
    /// method already holds the authoritative row and a location's accepted images are told apart only by the name an
    /// operator typed. Without it a multi-image location renders as several references all called "location
    /// continuity", which is the same unusable record the picker had before names existed.
    /// </para>
    ///
    /// <para>
    /// An unnamed image degrades to the element's own role, exactly as it read before names existed: nothing is
    /// invented to fill the gap.
    /// </para>
    /// </summary>
    private static string DescribeReference(string semanticRole, string? displayName)
    {
        var role = semanticRole?.Trim() ?? string.Empty;
        var name = displayName?.Trim() ?? string.Empty;
        if (name.Length == 0) return role;
        return role.Length == 0 ? name : $"{role} ({name})";
    }

    /// <summary>
    /// Instruction for a reference-conditioned run: the source image is the base, references are
    /// guidance only, and unrelated detail is preserved.
    /// </summary>
    public static string BuildReferenceAwareInstruction(
        string instruction,
        IReadOnlyList<ReferenceApplicationSelection> applications)
    {
        var identityApplications = applications
            .Where(application => application.AssetType == SceneAssetType.CharacterFace
                || string.Equals(application.ElementKey, "Identity", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var identityConstraint = identityApplications.Count == 0
            ? string.Empty
            : " Identity references are face-local guidance only: change only the selected character face identity in the existing scene. Treat the existing scene's visible neck and body skin tone as authoritative and harmonize the corrected face's skin tone, undertone, exposure, and shading with that body under the scene lighting; do not import a mismatched complexion from the reference, and do not copy the reference image's body, pose, clothing, framing, background, lighting, or composition.";

        return $"The first input image is the existing scene and is the base image. Additional reference images are guidance only, never replacement images. {instruction.Trim()}{identityConstraint} Preserve all unrelated people, objects, scene geometry, framing, crop, lighting, colors, and composition exactly unless the instruction explicitly requests that specific change.";
    }
}
