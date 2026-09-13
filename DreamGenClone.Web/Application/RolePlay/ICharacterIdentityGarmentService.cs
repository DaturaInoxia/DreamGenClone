using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The GarmentRemoval step of the Character Identity pipeline (B-121 Phase E, FR21-016/017).
/// The edit itself is performed by the app-wide unified image-edit workbench — this service only
/// resolves the step's inputs and configuration, so no step-specific edit form exists.
///
/// The step's outcome is deliberately NOT recorded here. De-clothe, crop and enhance are recorded together
/// when the user approves the canonical front in Panel B
/// (<see cref="ICharacterIdentityBuildService.SetCanonicalFrontAsync"/>), which derives all three from the
/// approved image's own lineage. A second writer could disagree with that derivation, so there is one.
/// </summary>
public interface ICharacterIdentityGarmentService
{
    /// <summary>The resolved <c>identity.garment.remove</c> instruction to seed the shared edit workbench.</summary>
    Task<string> ResolveGarmentPromptAsync(
        string characterId,
        string? characterName,
        string? gender,
        CancellationToken cancellationToken = default);

    /// <summary>The configured editor model id. Fails fast naming <c>EditorModelId</c> when unset.</summary>
    Task<string> ResolveEditorModelIdAsync(CancellationToken cancellationToken = default);

    /// <summary>The image the step operates on: the completed Front step's output artifact.</summary>
    Task<SceneAssetImage> ResolveGarmentSourceAsync(string buildId, CancellationToken cancellationToken = default);
}
