using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Components.Shared;

/// <summary>One accepted image of an asset, offered to a reference slot.</summary>
public sealed record SceneAssetReferenceChoice(SceneAsset Asset, SceneAssetImage Image)
{
    /// <summary>
    /// What the operator calls this image, or null when they have not named it.
    ///
    /// <para>
    /// The ONE owner of the label a choice shows, so the picker's option text and the binding's recorded
    /// <c>ReferenceLabel</c> cannot disagree about what the operator selected. A location holds several accepted
    /// images and this name is the only thing that tells them apart, which is why it is read here rather than
    /// re-derived at each surface.
    /// </para>
    /// </summary>
    public string? Name => string.IsNullOrWhiteSpace(Image.DisplayName) ? null : Image.DisplayName.Trim();
}