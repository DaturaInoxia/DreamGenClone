namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The body view acquisition settings in force for one character (B-122 E-2): the image model and the render size.
/// Both live in the persisted workflow settings (<c>ReferenceWorkflowSettings.BodyModelId</c> /
/// <c>BodyImageSize</c>), so the studio sets them once and every view of that body is rendered the same way.
/// </summary>
public sealed record CharacterBodyViewSettings(string ModelId, string ImageSize)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(ModelId) && !string.IsNullOrWhiteSpace(ImageSize);
}
