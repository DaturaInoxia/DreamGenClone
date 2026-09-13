using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// Persistence for the editable workflow prompt templates and reference-workflow settings. Schema is
/// created and seeded idempotently (the seed is migration data, never a runtime fallback).
/// </summary>
public interface IImageWorkflowRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<ImageWorkflowPromptTemplate?> GetTemplateAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageWorkflowPromptTemplate>> ListTemplatesAsync(CancellationToken cancellationToken = default);

    Task UpsertTemplateAsync(ImageWorkflowPromptTemplate template, CancellationToken cancellationToken = default);

    Task<ReferenceWorkflowSettings?> GetSettingsAsync(string id, CancellationToken cancellationToken = default);

    Task UpsertSettingsAsync(ReferenceWorkflowSettings settings, CancellationToken cancellationToken = default);
}
