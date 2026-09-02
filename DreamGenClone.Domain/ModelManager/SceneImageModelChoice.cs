namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// Lightweight, UI-facing choice describing one enabled image model for the Studio model selector.
/// </summary>
public sealed record SceneImageModelChoice(
    string ModelId,
    string DisplayName,
    string ModelIdentifier,
    string ProviderName,
    bool HasIdentity);
