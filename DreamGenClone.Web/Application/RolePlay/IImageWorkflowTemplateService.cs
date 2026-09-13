using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Resolves workflow prompt templates and settings from persisted configuration. Resolution is
/// character override → global → fail fast naming the key; there is no code-embedded fallback.
/// </summary>
public interface IImageWorkflowTemplateService
{
    Task<ImageWorkflowPromptTemplate> ResolveAsync(
        string key, string? characterProfileId, CancellationToken cancellationToken = default);

    Task<ImageWorkflowPromptTemplate> ResetToSeedAsync(
        string key,
        ImageWorkflowPromptTemplateScope scope,
        string? characterProfileId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageWorkflowPromptTemplate>> ListTemplatesAsync(
        string? characterProfileId, CancellationToken cancellationToken = default);

    Task SaveTemplateAsync(ImageWorkflowPromptTemplate template, CancellationToken cancellationToken = default);

    Task<ReferenceWorkflowSettings> ResolveSettingsAsync(
        string? characterProfileId, CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(ReferenceWorkflowSettings settings, CancellationToken cancellationToken = default);
}
