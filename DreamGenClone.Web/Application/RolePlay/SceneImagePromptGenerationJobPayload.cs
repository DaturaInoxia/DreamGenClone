namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Payload for the SceneImagePromptGeneration background job.</summary>
public sealed class SceneImagePromptGenerationJobPayload
{
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string PromptRecordId { get; set; } = string.Empty;

    /// <summary>
    /// The Studio model dropdown selection at enqueue time. The generation job prefers it when its
    /// family matches the prompt record's style; otherwise it resolves the first enabled model of
    /// that style so the correct compiler/content policy runs.
    /// </summary>
    public string? RequestedImageModelId { get; set; }

    /// <summary>
    /// The ordered reference bindings this step will render with, when the caller already knows them. A binding
    /// that supplies an element - a location image, a face - means the prompt must not ALSO describe that element,
    /// so these become removals through the same path an operator-authored removal takes. Null when the caller has
    /// no bindings yet, which is every non-production render.
    /// </summary>
    public IReadOnlyList<ReferenceApplicationSelection>? ReferenceApplications { get; set; }
}
