using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Application.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class CharacterIdentityPromotionService : ICharacterIdentityPromotionService
{
    private static readonly (CharacterIdentityAngleView? View, string Label)[] RequiredViews =
    [
        (null, "Front"),
        (CharacterIdentityAngleView.ThreeQuarterLeft, "Three-quarter left"),
        (CharacterIdentityAngleView.ThreeQuarterRight, "Three-quarter right"),
        (CharacterIdentityAngleView.ProfileLeft, "Profile left"),
        (CharacterIdentityAngleView.ProfileRight, "Profile right")
    ];

    private readonly ICharacterIdentityBuildRepository _repository;
    private readonly ICharacterIdentityBuildService _builds;
    private readonly ICharacterImageIdentityService _identity;
    private readonly ISceneAssetService _assets;

    public CharacterIdentityPromotionService(
        ICharacterIdentityBuildRepository repository,
        ICharacterIdentityBuildService builds,
        ICharacterImageIdentityService identity,
        ISceneAssetService assets)
    {
        _repository = repository;
        _builds = builds;
        _identity = identity;
        _assets = assets;
    }

    public async Task<CharacterIdentityPackPromotionResult> GetReadinessAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var build = await _builds.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
        var steps = await _builds.ListStepsAsync(build.Id, cancellationToken);
        var angles = await _repository.ListAnglesAsync(build.Id, cancellationToken);
        var views = new List<CharacterIdentityPackPromotionView>();
        var reasons = new List<string>();

        var front = build.CanonicalFrontAssetId;
        views.Add(new(null, "Front", front ?? string.Empty, !string.IsNullOrWhiteSpace(front)));
        if (front is null)
            reasons.Add("Front: canonical front is missing.");

        foreach (var required in RequiredViews.Where(item => item.View is not null))
        {
            var angle = angles.FirstOrDefault(item => item.View == required.View);
            var artifact = angle?.AcceptedAttemptId is { Length: > 0 } acceptedId
                ? (await _repository.ListAngleAttemptsAsync(angle.Id, cancellationToken)).FirstOrDefault(a => a.Id == acceptedId)?.OutputArtifactId
                : null;
            var ready = angle?.Status == CharacterIdentityAngleStatus.Accepted && !string.IsNullOrWhiteSpace(artifact);
            views.Add(new(required.View, required.Label, artifact ?? string.Empty, ready));
            if (!ready)
                reasons.Add($"{required.Label}: no accepted attempt.");
        }

        var anglesStep = steps.FirstOrDefault(step => step.Step == CharacterIdentityBuildStep.Angles);
        if (anglesStep?.Status != CharacterIdentityBuildStepStatus.Complete)
            reasons.Add("Angles: the angle step is not complete.");

        return new(reasons.Count == 0, build.ProducedIdentityPackId, views, reasons);
    }

    public async Task<CharacterIdentityPackPromotionResult> PromoteAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var readiness = await GetReadinessAsync(buildId, cancellationToken);
        if (!readiness.Ready)
            throw new InvalidOperationException(string.Join(" ", readiness.BlockingReasons));

        var build = await _builds.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
        if (!string.IsNullOrWhiteSpace(build.ProducedIdentityPackId))
        {
            var existing = await _identity.GetPackAsync(build.ProducedIdentityPackId, cancellationToken);
            if (existing is not null)
                return readiness with { PackId = existing.Id };
        }
        var pack = await _identity.CreateDraftPackAsync(build.CharacterProfileId, cancellationToken);
        foreach (var view in readiness.Views)
        {
            var image = await _assets.GetImageAsync(view.ArtifactId, cancellationToken)
                ?? throw new InvalidOperationException($"Promotion image '{view.ArtifactId}' for {view.Label} was not found.");
            if (string.IsNullOrWhiteSpace(image.FileRelativePath))
                throw new InvalidOperationException($"Promotion image '{view.ArtifactId}' for {view.Label} has no stored file.");
            var downloaded = await _assets.OpenImageForDownloadAsync(image.Id, cancellationToken);
            await using var content = downloaded.Stream;
            var faceView = view.View switch
            {
                null => SceneImageReferenceFaceView.Front,
                CharacterIdentityAngleView.ThreeQuarterLeft => SceneImageReferenceFaceView.ThreeQuarterLeft,
                CharacterIdentityAngleView.ThreeQuarterRight => SceneImageReferenceFaceView.ThreeQuarterRight,
                CharacterIdentityAngleView.ProfileLeft => SceneImageReferenceFaceView.ProfileLeft,
                CharacterIdentityAngleView.ProfileRight => SceneImageReferenceFaceView.ProfileRight,
                _ => throw new InvalidOperationException($"Unsupported promotion view '{view.View}'.")
            };
            var asset = await _identity.UploadAssetAsync(pack.Id, SceneImageReferenceAssetKind.Face, $"{view.Label}.png", content, faceView, cancellationToken: cancellationToken);
            await _identity.SetAssetProvenanceAsync(asset.Id, $"Character identity build {build.Id}; {view.Label}", SceneImageReferenceConsentState.NotApplicable, cancellationToken);
        }

        build.ProducedIdentityPackId = pack.Id;
        await _repository.UpsertBuildAsync(build, cancellationToken);
        return readiness with { PackId = pack.Id };
    }
}