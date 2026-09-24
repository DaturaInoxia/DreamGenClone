using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class CharacterIdentityFrontService : ICharacterIdentityFrontService
{
    private readonly ICharacterIdentityBuildService _builds;
    private readonly ISceneAssetService _assets;
    private readonly IImageWorkflowTemplateService _templates;

    public CharacterIdentityFrontService(
        ICharacterIdentityBuildService builds,
        ISceneAssetService assets,
        IImageWorkflowTemplateService templates)
    {
        _builds = builds;
        _assets = assets;
        _templates = templates;
    }

    /// <summary>Prefix identifying a front-attempt candidate batch id.</summary>
    public const string FrontBatchPrefix = "front-";

    /// <summary>The deterministic candidate batch id shared by every front attempt of a build.</summary>
    public static string FrontBatchIdFor(string buildId) => $"{FrontBatchPrefix}{buildId.Trim()}";

    private const string FrontPromptKey = "identity.front.generate";

    public async Task<SceneAsset> EnsureFrontContainerAsync(
        string buildId, string containerName, CancellationToken cancellationToken = default)
    {
        var build = await _builds.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");

        if (!string.IsNullOrWhiteSpace(build.FrontContainerAssetId))
        {
            return await _assets.GetAssetAsync(build.FrontContainerAssetId, cancellationToken)
                ?? throw new InvalidOperationException($"Front container asset '{build.FrontContainerAssetId}' was not found.");
        }

        var container = await _assets.CreateAssetAsync(
            string.IsNullOrWhiteSpace(containerName) ? "Front" : containerName.Trim(),
            SceneAssetType.CharacterFace,
            cancellationToken: cancellationToken);
        await _builds.SetFrontContainerAsync(buildId, container.Id, cancellationToken);
        return container;
    }

    public async Task<string> ResolveFrontPromptAsync(
        string characterId, string? characterName, string description, CancellationToken cancellationToken = default)
    {
        var template = await _templates.ResolveAsync(FrontPromptKey, characterId, cancellationToken);
        return FillPrompt(template.Body, characterName, description);
    }

    public Task<ImageWorkflowPromptTemplate> ResolveFrontTemplateAsync(
        string characterId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            throw new InvalidOperationException("A character is required to resolve the front prompt.");
        return _templates.ResolveAsync(FrontPromptKey, characterId.Trim(), cancellationToken);
    }

    public async Task<bool> HasFrontPromptOverrideAsync(
        string characterId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            return false;

        var resolved = await _templates.ResolveAsync(FrontPromptKey, characterId.Trim(), cancellationToken);
        if (resolved.Scope != ImageWorkflowPromptTemplateScope.Character)
            return false;

        var global = await _templates.ResolveAsync(FrontPromptKey, null, cancellationToken);
        return !string.Equals(resolved.Body, global.Body, StringComparison.Ordinal);
    }

    public async Task SaveFrontPromptAsync(
        string characterId, string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            throw new InvalidOperationException("A character is required to save a front prompt.");
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("A front prompt is required.");

        var global = await _templates.ResolveAsync(FrontPromptKey, null, cancellationToken);
        var template = new ImageWorkflowPromptTemplate
        {
            Key = FrontPromptKey,
            Scope = ImageWorkflowPromptTemplateScope.Character,
            CharacterProfileId = characterId.Trim(),
            WorkflowStep = global.WorkflowStep,
            Body = prompt.Trim(),
            SeedBody = global.SeedBody
        };
        await _templates.SaveTemplateAsync(template, cancellationToken);
    }

    private static string FillPrompt(string body, string? characterName, string description)
    {
        var name = string.IsNullOrWhiteSpace(characterName) ? "the subject" : characterName.Trim();
        var text = body.Replace("{CharacterName}", name, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(description))
        {
            text = text.Replace(": {Description}", string.Empty, StringComparison.Ordinal);
        }
        else
        {
            text = text.Replace("{Description}", description.Trim(), StringComparison.Ordinal);
        }

        return text.Trim();
    }

    public async Task<string> ResetFrontPromptToDefaultAsync(
        string characterId, string? characterName, string description, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            throw new InvalidOperationException("A character is required to reset the front prompt.");

        var reset = await _templates.ResetToSeedAsync(
            FrontPromptKey, ImageWorkflowPromptTemplateScope.Character, characterId.Trim(), cancellationToken);
        return FillPrompt(reset.Body, characterName, description);
    }

    public async Task<string?> ResolveFrontModelAsync(string characterId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            return null;

        var settings = await _templates.ResolveSettingsAsync(characterId.Trim(), cancellationToken);
        return settings.FrontModelId;
    }

    public async Task SaveFrontModelAsync(string characterId, string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            throw new InvalidOperationException("A character is required to save the front model.");
        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException("A front model is required.");

        var resolved = await _templates.ResolveSettingsAsync(characterId.Trim(), cancellationToken);
        var settings = new ReferenceWorkflowSettings
        {
            CharacterProfileId = characterId.Trim(),
            EditorModelId = resolved.EditorModelId,
            FrontModelId = modelId.Trim(),
            UpscalerModelName = resolved.UpscalerModelName,
            EnhanceTargetLongEdge = resolved.EnhanceTargetLongEdge,
            EyeGateMaxAbsIrisDyPercent = resolved.EyeGateMaxAbsIrisDyPercent,
            AngleYawMinAbsPercent = resolved.AngleYawMinAbsPercent,
            QualityGateMinSharpness = resolved.QualityGateMinSharpness,
            CropHeadroomPercent = resolved.CropHeadroomPercent,
            CropTargetAspect = resolved.CropTargetAspect,
            DeriveByMirrorThreeQuarterRight = resolved.DeriveByMirrorThreeQuarterRight,
            DeriveByMirrorProfileRight = resolved.DeriveByMirrorProfileRight,
            DeriveByMirrorThreeQuarterLeft = resolved.DeriveByMirrorThreeQuarterLeft,
            DeriveByMirrorProfileLeft = resolved.DeriveByMirrorProfileLeft,
            EyeToolPythonPath = resolved.EyeToolPythonPath
        };
        await _templates.SaveSettingsAsync(settings, cancellationToken);
    }

    public async Task<IReadOnlyList<SceneAssetImage>> ListFrontAttemptsAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var build = await _builds.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
        if (string.IsNullOrWhiteSpace(build.FrontContainerAssetId))
        {
            return [];
        }

        return await _assets.ListImagesAsync(build.FrontContainerAssetId, cancellationToken);
    }

    public async Task<SceneAssetImage> GenerateFrontAttemptAsync(
        string buildId,
        string containerName,
        string prompt,
        string modelId,
        string imageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("A front prompt is required.");
        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException("An exact image model is required.");

        var container = await EnsureFrontContainerAsync(buildId, containerName, cancellationToken);
        return await _assets.AddGeneratedImageAsync(
            container.Id, prompt.Trim(), modelId.Trim(), imageSize.Trim(),
            cancellationToken: cancellationToken,
            candidateBatchId: FrontBatchIdFor(buildId));
    }

    public async Task<SceneAssetImage> UploadFrontAttemptAsync(
        string buildId,
        string containerName,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var container = await EnsureFrontContainerAsync(buildId, containerName, cancellationToken);
        return await _assets.AddUploadedImageAsync(
            container.Id, fileName, content, cancellationToken, candidateBatchId: FrontBatchIdFor(buildId));
    }

    public async Task<CharacterIdentityBuild> SelectFrontAsync(
        string buildId, string imageId, CancellationToken cancellationToken = default)
    {
        var build = await _builds.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
        if (string.IsNullOrWhiteSpace(build.FrontContainerAssetId))
            throw new InvalidOperationException("This build has no front container yet.");

        var image = await _assets.GetImageAsync(imageId, cancellationToken)
            ?? throw new InvalidOperationException($"Front image '{imageId}' was not found.");
        if (!string.Equals(image.AssetId, build.FrontContainerAssetId, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected image does not belong to this build's front container.");
        if (image.Status != SceneAssetStatus.Complete)
            throw new InvalidOperationException("The selected front image is not complete yet.");

        return await _builds.CompleteStepAsync(
            buildId,
            CharacterIdentityBuildStep.Front,
            inputArtifactId: null,
            outputArtifactId: imageId,
            resolvedPromptText: image.Prompt,
            resolvedModelId: ExtractRequestedModelId(image),
            cancellationToken: cancellationToken);
    }

    private static string? ExtractRequestedModelId(SceneAssetImage image)
    {
        if (string.IsNullOrWhiteSpace(image.AssociationMetadataJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(image.AssociationMetadataJson);
            if (doc.RootElement.TryGetProperty("requestedModelId", out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON metadata (e.g. an upload) has no requested model id.
        }

        return null;
    }
}
