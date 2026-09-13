using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// GarmentRemoval step orchestration. The step does <b>not</b> own an edit form: the user performs
/// the edit in the app-wide unified image-edit workbench, and this service resolves the seeded
/// instruction/editor and the image the step operates on.
///
/// Recording the step's outcome is not part of this service: de-clothe, crop and enhance are recorded
/// together when the canonical front is approved (see
/// <see cref="CharacterIdentityBuildService.SetCanonicalFrontAsync"/>), from the approved image's lineage.
/// </summary>
public sealed class CharacterIdentityGarmentService : ICharacterIdentityGarmentService
{
    private const string GarmentRemoveKey = "identity.garment.remove";
    private const string PronounPlaceholder = "{SubjectPronounPossessive}";

    private readonly ICharacterIdentityBuildRepository _repository;
    private readonly IImageWorkflowTemplateService _templates;
    private readonly ISceneAssetService _assets;

    public CharacterIdentityGarmentService(
        ICharacterIdentityBuildRepository repository,
        IImageWorkflowTemplateService templates,
        ISceneAssetService assets)
    {
        _repository = repository;
        _templates = templates;
        _assets = assets;
    }

    public async Task<string> ResolveGarmentPromptAsync(
        string characterId,
        string? characterName,
        string? gender,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            throw new InvalidOperationException("A character is required to resolve the garment-removal prompt.");
        }

        var template = await _templates.ResolveAsync(GarmentRemoveKey, characterId.Trim(), cancellationToken);
        var possessive = ResolvePossessivePronoun(characterName, gender);
        return template.Body
            .Replace(PronounPlaceholder, possessive, StringComparison.Ordinal)
            .Replace("{CharacterName}", string.IsNullOrWhiteSpace(characterName) ? "the subject" : characterName.Trim(), StringComparison.Ordinal)
            .Trim();
    }

    public async Task<string> ResolveEditorModelIdAsync(CancellationToken cancellationToken = default)
    {
        // EditorModelId is declared global-scope in seed-prompts.md §2, so the global row is
        // authoritative; a per-character settings row must not shadow it.
        var settings = await _templates.ResolveSettingsAsync(null, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.EditorModelId))
        {
            throw new InvalidOperationException(
                "Missing required configuration 'EditorModelId': the image editor model must be configured "
                + "in the global reference workflow settings before the Garment removal step can run.");
        }

        return settings.EditorModelId.Trim();
    }

    public async Task<SceneAssetImage> ResolveGarmentSourceAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var steps = await LoadStepsAsync(buildId, cancellationToken);
        var frontArtifactId = RequireFrontArtifactId(steps);
        return await _assets.GetImageAsync(frontArtifactId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The front image '{frontArtifactId}' was not found in the asset library.");
    }

    private async Task<(CharacterIdentityBuild Build, IReadOnlyList<CharacterIdentityBuildStepRecord> Steps)> LoadAsync(
        string buildId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buildId))
        {
            throw new InvalidOperationException("A build id is required.");
        }

        var build = await _repository.GetBuildAsync(buildId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
        var steps = await _repository.ListStepsAsync(build.Id, cancellationToken);
        return (build, steps);
    }

    private async Task<IReadOnlyList<CharacterIdentityBuildStepRecord>> LoadStepsAsync(
        string buildId, CancellationToken cancellationToken)
        => (await LoadAsync(buildId, cancellationToken)).Steps;

    private static string RequireFrontArtifactId(IReadOnlyList<CharacterIdentityBuildStepRecord> steps)
    {
        var front = RequireRow(steps, CharacterIdentityBuildStep.Front);
        if (front.Status != CharacterIdentityBuildStepStatus.Complete)
        {
            throw new InvalidOperationException(
                "The garment-removal step requires a completed Front step.");
        }

        if (string.IsNullOrWhiteSpace(front.OutputArtifactId))
        {
            throw new InvalidOperationException("The completed Front step has no output artifact.");
        }

        return front.OutputArtifactId.Trim();
    }

    private static CharacterIdentityBuildStepRecord RequireRow(
        IReadOnlyList<CharacterIdentityBuildStepRecord> steps, CharacterIdentityBuildStep step)
        => steps.FirstOrDefault(row => row.Step == step)
            ?? throw new InvalidOperationException($"The build has no recorded row for step '{step}'.");

    /// <summary>
    /// Resolve the template's possessive pronoun deterministically: the character's configured
    /// gender when known, otherwise the character's own name. No value is guessed.
    /// </summary>
    private static string ResolvePossessivePronoun(string? characterName, string? gender)
    {
        if (string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase))
        {
            return "his";
        }

        if (string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase))
        {
            return "her";
        }

        if (!string.IsNullOrWhiteSpace(characterName))
        {
            return $"{characterName.Trim()}'s";
        }

        throw new InvalidOperationException(
            "Cannot resolve '{SubjectPronounPossessive}' in the garment-removal prompt: the character has no "
            + "configured gender and no name.");
    }
}
