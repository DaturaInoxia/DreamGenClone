using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Resolver for the editable workflow prompt templates and settings. Every prompt a pipeline step
/// uses is a persisted row; resolution is character override → global → fail fast naming the key.
/// </summary>
public sealed class ImageWorkflowTemplateService : IImageWorkflowTemplateService
{
    private readonly IImageWorkflowRepository _repository;

    public ImageWorkflowTemplateService(IImageWorkflowRepository repository)
    {
        _repository = repository;
    }

    public async Task<ImageWorkflowPromptTemplate> ResolveAsync(
        string key, string? characterProfileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("A template key is required.");
        var trimmedKey = key.Trim();

        if (!string.IsNullOrWhiteSpace(characterProfileId))
        {
            var characterId = ImageWorkflowPromptTemplate.ComputeId(trimmedKey, ImageWorkflowPromptTemplateScope.Character, characterProfileId);
            var characterTemplate = await _repository.GetTemplateAsync(characterId, cancellationToken);
            if (characterTemplate is not null)
            {
                return characterTemplate;
            }
        }

        var globalId = ImageWorkflowPromptTemplate.ComputeId(trimmedKey, ImageWorkflowPromptTemplateScope.Global, null);
        return await _repository.GetTemplateAsync(globalId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Missing required workflow prompt template: '{trimmedKey}'. No global row and no character override exist for this key.");
    }

    public async Task<ImageWorkflowPromptTemplate> ResetToSeedAsync(
        string key,
        ImageWorkflowPromptTemplateScope scope,
        string? characterProfileId,
        CancellationToken cancellationToken = default)
    {
        var trimmedKey = key.Trim();
        if (scope == ImageWorkflowPromptTemplateScope.Global)
        {
            var global = await ResolveAsync(trimmedKey, null, cancellationToken);
            global.Body = global.SeedBody;
            global.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertTemplateAsync(global, cancellationToken);
            return global;
        }

        // A character override resets to the global default.
        var globalDefault = await ResolveAsync(trimmedKey, null, cancellationToken);
        var overrideTemplate = new ImageWorkflowPromptTemplate
        {
            Key = trimmedKey,
            Scope = ImageWorkflowPromptTemplateScope.Character,
            CharacterProfileId = characterProfileId,
            WorkflowStep = globalDefault.WorkflowStep,
            Body = globalDefault.Body,
            SeedBody = globalDefault.SeedBody,
            UpdatedUtc = DateTime.UtcNow
        };
        overrideTemplate.Id = ImageWorkflowPromptTemplate.ComputeId(trimmedKey, ImageWorkflowPromptTemplateScope.Character, characterProfileId);
        await _repository.UpsertTemplateAsync(overrideTemplate, cancellationToken);
        return overrideTemplate;
    }

    public async Task<IReadOnlyList<ImageWorkflowPromptTemplate>> ListTemplatesAsync(
        string? characterProfileId, CancellationToken cancellationToken = default)
    {
        var all = await _repository.ListTemplatesAsync(cancellationToken);
        return all
            .Where(t => t.Scope == ImageWorkflowPromptTemplateScope.Global
                || (t.Scope == ImageWorkflowPromptTemplateScope.Character
                    && string.Equals(t.CharacterProfileId, characterProfileId, StringComparison.Ordinal)))
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .ThenBy(t => t.Scope)
            .ToList();
    }

    public async Task SaveTemplateAsync(ImageWorkflowPromptTemplate template, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(template.Key))
            throw new InvalidOperationException("A template key is required.");
        if (string.IsNullOrWhiteSpace(template.Body))
            throw new InvalidOperationException($"Template body is required (key '{template.Key}').");

        template.Key = template.Key.Trim();
        template.Id = ImageWorkflowPromptTemplate.ComputeId(template.Key, template.Scope, template.CharacterProfileId);
        template.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertTemplateAsync(template, cancellationToken);
    }

    public async Task<ReferenceWorkflowSettings> ResolveSettingsAsync(
        string? characterProfileId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(characterProfileId))
        {
            var characterSettings = await _repository.GetSettingsAsync(
                ReferenceWorkflowSettings.ComputeId(characterProfileId), cancellationToken);
            if (characterSettings is not null)
            {
                return characterSettings;
            }
        }

        return await _repository.GetSettingsAsync("global", cancellationToken)
            ?? throw new InvalidOperationException("Missing ReferenceWorkflowSettings: the global row is required.");
    }

    public async Task SaveSettingsAsync(ReferenceWorkflowSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Id = ReferenceWorkflowSettings.ComputeId(settings.CharacterProfileId);
        settings.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertSettingsAsync(settings, cancellationToken);
    }
}
