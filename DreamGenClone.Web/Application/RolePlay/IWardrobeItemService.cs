using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>One image of a wardrobe item, with the two facts the tab shows about it.</summary>
/// <param name="Image">The stored image.</param>
/// <param name="Label">
/// The operator's own name for this image of the item ("front", "back", "worn"), or an empty string when they have
/// not named it. The picker falls back to a positional name so two images of one item are still tellable apart.
/// </param>
/// <param name="InUse">Whether this image is currently usable as a reference image.</param>
public sealed record WardrobeItemImage(SceneAssetImage Image, string Label, bool InUse);

/// <summary>
/// A wardrobe item: ONE garment, with however many images of it the operator kept. Items are SHARED — the same
/// sundress serves whoever the scene calls for — so the item carries no character.
/// </summary>
public sealed record WardrobeItem(SceneAsset Asset, IReadOnlyList<WardrobeItemImage> Images)
{
    public IReadOnlyList<WardrobeItemImage> InUseImages => Images.Where(image => image.InUse).ToList();

    /// <summary>True when the item has at least one image a reference slot can bind.</summary>
    public bool IsUsable => InUseImages.Count > 0;
}

/// <summary>
/// The wardrobe library: create a garment item, keep the images of it the operator wants, and make an image usable
/// as a reference image.
/// </summary>
/// <remarks>
/// Deliberately NOT character-scoped and deliberately NOT a versioned, approval-reviewed aggregate. A wardrobe item
/// is a shared library entry — the same dress is worn by more than one character, so hanging it off one character is
/// wrong (found 2026-09-29: a character-scoped picker made an unowned item reachable by nobody). And the review,
/// license and consent ceremony that guards a character's own approved references is not what the operator needs
/// here: what they need is "this is the dress", so <see cref="SetImageInUseAsync"/> writes the platform-required
/// provenance itself, in one click, and never asks them to type any of it.
/// </remarks>
public interface IWardrobeItemService
{
    /// <summary>Every wardrobe item with its images, in a stable display order.</summary>
    Task<IReadOnlyList<WardrobeItem>> ListItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates one shared wardrobe item. Fail fast on a blank name, and on a name another FINAL item already owns
    /// (an item is how the operator refers to a garment, so two items cannot share one).
    /// </summary>
    Task<SceneAsset> CreateItemAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes an image usable as a reference image, or stops it being one. The platform's provenance fields are
    /// written here, so the operator never fills in a form; stopping first is what makes the image deletable.
    /// </summary>
    Task<SceneAssetImage> SetImageInUseAsync(
        string imageId,
        bool inUse,
        CancellationToken cancellationToken = default);

    /// <summary>Names one image of the item ("front"), or clears the name when blank.</summary>
    Task SetImageLabelAsync(string imageId, string? label, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one image. Refused while it is in use as a reference, the same rule every asset-image delete follows.
    /// </summary>
    Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The image models this tab can drive: every enabled image model whose family and dialect has a wardrobe-item
    /// prompt compiler. A model this tab cannot compile for is not offered rather than offered and then refused.
    /// </summary>
    Task<IReadOnlyList<SceneImageModelChoice>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The configured default model for this tab (the <c>RolePlayWardrobeItem</c> function default), or null when
    /// none is configured. Configuration, not a hardcoded model: changing it in Model Manager changes this answer.
    /// </summary>
    Task<string?> DefaultModelIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts <paramref name="outputCount"/> renders of one wardrobe item: each compiles the operator's short
    /// description into a full reference prompt GEARED TO <paramref name="modelId"/>'s family, then renders it.
    /// </summary>
    Task EnqueueItemImagesAsync(
        string assetId,
        string itemDescription,
        string modelId,
        string imageSize,
        int outputCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drafts ONE prompt for the item and stops there: the row shows its prompt for the operator to read and correct,
    /// and no render is queued. Data rather than a second job type, because this is the same compile as
    /// <see cref="EnqueueItemImagesAsync"/> followed by a decision the operator has not made yet.
    /// </summary>
    Task<SceneAssetImage> EnqueuePromptDraftAsync(
        string assetId,
        string itemDescription,
        string modelId,
        string imageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an ALREADY MODEL-READY prompt as a new image of the item and starts its render.
    /// </summary>
    /// <remarks>
    /// The single entrance to the pipeline, used by the compile job (for the prompt it drafted) and by the tab (for a
    /// prompt the operator edited by hand). Both must enter here, or one path would skip a rule the other applies — and
    /// the prompt is stored with the compiler that authored it, which is what makes the render use the text verbatim
    /// instead of compiling it a second time.
    /// </remarks>
    Task<SceneAssetImage> RenderPromptAsync(
        string assetId,
        string prompt,
        string modelId,
        string imageSize,
        string promptCompilerId,
        string? semanticDescription = null,
        string? handEditedFromImageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the render of an image row that already carries its final, model-ready prompt (the compile job has just
    /// written it). The row must name a prompt compiler: that is what tells the render path to use the text verbatim.
    /// </summary>
    Task RenderExistingImageAsync(
        string imageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a compiled row as waiting for the operator's decision rather than for a render, so the tab can say so
    /// instead of leaving a drafted prompt looking like work still in flight.
    /// </summary>
    Task MarkPromptReadyAsync(
        string imageId,
        CancellationToken cancellationToken = default);
}
