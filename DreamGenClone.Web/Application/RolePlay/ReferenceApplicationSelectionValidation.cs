using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The rules a step's reference bindings must satisfy before they are persisted as an image's provenance.
///
/// <para>
/// <b>Why one place.</b> This rule had been written three times — in <c>SceneAssetService</c>, in
/// <c>SceneAssetImageEditCompilationService</c> and in <c>SceneImageService</c> — and all three copies knew only the
/// legacy reference channel: an approved SCENE ASSET, or text. A binding that named an identity-PACK reference, or a
/// pose skeleton, was refused as "requires an approved asset for strategy 'NativeMultiReference'" even though the
/// render supports both and the operator had just selected one (reported live 2026-09-30: "Asset reference application
/// 'Body' requires an approved asset for strategy 'NativeMultiReference' I selected a body").
/// </para>
///
/// <para>
/// The rule those copies were trying to state is a REPRODUCIBILITY rule, not a scene-asset rule: a structural binding
/// must name an exact source, so the render can be repeated from the record. Any of the channels the render accepts
/// satisfies it, because each one names something that does not drift.
/// </para>
/// </summary>
public static class ReferenceApplicationSelectionValidation
{
    /// <summary>
    /// Validates the bindings for one request, or throws naming the element that is wrong.
    /// </summary>
    /// <param name="applications">The step's bindings, in frame order.</param>
    /// <param name="label">
    /// What the caller calls these, for the message: "Reference" on the scene path, "Asset reference" on the asset
    /// paths. It is a parameter rather than a second copy of the rule.
    /// </param>
    public static void Validate(
        IReadOnlyList<ReferenceApplicationSelection> applications,
        string label = "Reference")
    {
        ArgumentNullException.ThrowIfNull(applications);

        if (applications.Count == 0)
        {
            return;
        }

        if (applications.Any(application => string.IsNullOrWhiteSpace(application.ElementKey)
            || string.IsNullOrWhiteSpace(application.SemanticRole)
            || string.IsNullOrWhiteSpace(application.Strategy)))
        {
            throw new InvalidOperationException(
                $"Every {label.ToLowerInvariant()} application requires an element key, semantic role, and strategy.");
        }

        if (applications.Select(application => application.ElementKey.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != applications.Count)
        {
            throw new InvalidOperationException($"{label} application element keys must be unique.");
        }

        foreach (var application in applications)
        {
            var element = application.ElementKey.Trim();

            // An approved scene asset is pinned by the exact image, its production version and its checksum: any of the
            // three missing means the record names an image that could later be swapped underneath it.
            var hasSceneAsset = !string.IsNullOrWhiteSpace(application.SceneAssetId);
            if (hasSceneAsset
                && (string.IsNullOrWhiteSpace(application.SceneAssetImageId)
                    || application.SceneAssetVersion is null
                    || string.IsNullOrWhiteSpace(application.SceneAssetSha256)))
            {
                throw new InvalidOperationException(
                    $"{label} application '{element}' is missing an exact approved asset version or checksum.");
            }

            // A pack image is named by its PACK and its asset id inside that pack. One without the other is not an
            // image: a pack id alone is a container, and a pack asset id is only meaningful inside its pack.
            var hasPack = !string.IsNullOrWhiteSpace(application.IdentityPackId);
            if (hasPack && string.IsNullOrWhiteSpace(application.ReferenceAssetId))
            {
                throw new InvalidOperationException(
                    $"{label} application '{element}' names identity pack '{application.IdentityPackId}' but no "
                    + "reference asset inside it, so there is no image to condition on.");
            }

            // A pose travels as a skeleton, and a skeleton has no version or checksum of its own - the preset ID is what
            // makes it exact, because the render reads the skeleton BY ID so a stale path cannot point at another file.
            var hasPose = !string.IsNullOrWhiteSpace(application.PosePresetId)
                || !string.IsNullOrWhiteSpace(application.SkeletonRelativePath);

            var isTextOnly = string.Equals(
                application.Strategy, ReferenceStrategyCatalogue.TextOnly, StringComparison.OrdinalIgnoreCase);

            if (!isTextOnly && !hasSceneAsset && !hasPack && !hasPose)
            {
                throw new InvalidOperationException(
                    $"{label} application '{element}' declares strategy '{application.Strategy}' but names no reference "
                    + "image. A binding that carries an element structurally must name one: an approved scene asset "
                    + "image with its version and checksum, an identity-pack reference (pack id and reference asset id), "
                    + "or a pose (its preset id or skeleton path).");
            }

            if (application.Strength is < 0m or > 1m)
            {
                throw new InvalidOperationException(
                    $"{label} application '{element}' strength must be between 0 and 1.");
            }
        }
    }
}
