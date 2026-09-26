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

    /// <summary>
    /// The visual strategies this model can actually execute (<c>TextOnly</c> plus each declared and qualified
    /// graph strategy), so a reference panel offers the selected model's real capabilities instead of a list frozen
    /// per page. A model whose identity travels as its own reference images therefore offers
    /// <c>NativeMultiReference</c>, which a hardcoded text-only list had hidden (reported 2026-09-24: "the identity
    /// is not available to allow but it should be" with Qwen-Image-2.1 selected).
    /// </summary>
    public IReadOnlyList<string> QualifiedStrategies { get; init; } = [];

    /// <summary>
    /// The model's OWN configured ACCEPTANCE limit on reference image count, read from the same capability
    /// qualification the render reads (the <c>NativeMultiReference</c> entry). A host does not invent this number:
    /// the composer plans bindings from it, and the planner's measured pose ceiling is applied ON TOP of it rather
    /// than instead of it. <c>0</c> means this model declares no reference capacity at all - which is the honest
    /// answer for a model that carries identity some other way - and a step that offers reference slots on such a
    /// model must fail naming the missing configuration rather than guess a limit.
    /// </summary>
    public int MaxReferences { get; init; }
}
