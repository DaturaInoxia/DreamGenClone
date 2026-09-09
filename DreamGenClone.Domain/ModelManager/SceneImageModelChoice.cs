namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// Lightweight, UI-facing choice describing one enabled image model for the Studio model selector.
/// Carries the model's scene-image family + prompt dialect so the UI can show the matching
/// prompt tab (Pony tags vs natural language) for the model the user has selected.
/// </summary>
public sealed record SceneImageModelChoice(
    string ModelId,
    string DisplayName,
    string ModelIdentifier,
    string ProviderName,
    bool HasIdentity)
{
    public SceneImageModelFamily Family { get; init; } = SceneImageModelFamily.Unknown;

    public SceneImagePromptDialect Dialect { get; init; } = SceneImagePromptDialect.Unknown;
}
