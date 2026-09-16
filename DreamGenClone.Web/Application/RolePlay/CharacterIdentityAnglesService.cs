using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class CharacterIdentityAnglesService : ICharacterIdentityAnglesService
{
    private static readonly CharacterIdentityAngleView[] Ordered =
    [
        CharacterIdentityAngleView.ThreeQuarterLeft,
        CharacterIdentityAngleView.ThreeQuarterRight,
        CharacterIdentityAngleView.ProfileLeft,
        CharacterIdentityAngleView.ProfileRight
    ];

    private readonly ICharacterIdentityBuildRepository _repository;
    private readonly ICharacterIdentityBuildService _builds;
    private readonly ISceneAssetService _assets;
    private readonly IImageWorkflowTemplateService _templates;

    public CharacterIdentityAnglesService(
        ICharacterIdentityBuildRepository repository,
        ICharacterIdentityBuildService builds,
        ISceneAssetService assets,
        IImageWorkflowTemplateService templates)
    {
        _repository = repository;
        _builds = builds;
        _assets = assets;
        _templates = templates;
    }

    public Task<IReadOnlyList<CharacterIdentityAngleRecord>> ListAsync(
        string buildId, CancellationToken cancellationToken = default)
        => _repository.ListAnglesAsync(buildId, cancellationToken);

    public static string CandidateBatchIdFor(string buildId, CharacterIdentityAngleView view)
        => $"angles-{buildId}-{view.ToString().ToLowerInvariant()}";

    public async Task<IReadOnlyList<CharacterIdentityAngleAttempt>> ListAttemptsAsync(
        string buildId, CharacterIdentityAngleView view, CancellationToken cancellationToken = default)
    {
        var angle = (await _repository.ListAnglesAsync(buildId, cancellationToken)).FirstOrDefault(a => a.View == view);
        return angle is null ? [] : await _repository.ListAngleAttemptsAsync(angle.Id, cancellationToken);
    }

    public async Task<CharacterIdentityAngleRecord> PrepareAsync(
        string buildId,
        CharacterIdentityAngleView view,
        string modelId,
        string? promptOverride = null,
        CancellationToken cancellationToken = default)
    {
        var build = await _builds.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
        if (build.CurrentStep != CharacterIdentityBuildStep.Angles)
            throw new InvalidOperationException($"Face angles can only run at the Angles step; current step is {build.CurrentStep}.");

        var canonical = build.CanonicalFrontAssetId
            ?? throw new InvalidOperationException("Approve a canonical front before creating face angles.");
        var angles = (await _repository.ListAnglesAsync(build.Id, cancellationToken)).ToList();
        var sourceId = ResolveSource(view, canonical, angles);
        var key = PromptKey(view);
        var template = await _templates.ResolveAsync(key, build.CharacterProfileId, cancellationToken);
        var prompt = string.IsNullOrWhiteSpace(promptOverride) ? template.Body : promptOverride.Trim();
        var source = await _assets.GetImageAsync(sourceId, cancellationToken)
            ?? throw new InvalidOperationException($"Angle source image '{sourceId}' was not found.");

        var record = angles.FirstOrDefault(a => a.View == view)
            ?? new CharacterIdentityAngleRecord { BuildId = build.Id, View = view };
        if (record.Status == CharacterIdentityAngleStatus.NotStarted || record.Status == CharacterIdentityAngleStatus.Failed)
            record.InputArtifactId = source.Id;
        record.ResolvedPromptText = prompt;
        record.ResolvedModelId = modelId.Trim();
        record.FailureReason = null;
        record.ManualConfirmationRequired = view is CharacterIdentityAngleView.ProfileLeft or CharacterIdentityAngleView.ProfileRight;
        record.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertAngleAsync(record, cancellationToken);
        return record;
    }

    public async Task<CharacterIdentityAngleRecord> UploadAsync(
        string buildId,
        CharacterIdentityAngleView view,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var build = await _builds.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
        if (build.CurrentStep != CharacterIdentityBuildStep.Angles)
            throw new InvalidOperationException($"Face angles can only be uploaded at the Angles step; current step is {build.CurrentStep}.");
        var canonical = build.CanonicalFrontAssetId
            ?? throw new InvalidOperationException("Approve a canonical front before uploading face angles.");
        var angles = (await _repository.ListAnglesAsync(build.Id, cancellationToken)).ToList();
        var sourceId = ResolveSource(view, canonical, angles);
        var source = await _assets.GetImageAsync(sourceId, cancellationToken)
            ?? throw new InvalidOperationException($"Angle source image '{sourceId}' was not found.");
        var uploaded = await _assets.AddUploadedImageAsync(
            source.AssetId,
            fileName,
            content,
            cancellationToken,
            candidateBatchId: CandidateBatchIdFor(build.Id, view));

        var record = angles.FirstOrDefault(a => a.View == view)
            ?? new CharacterIdentityAngleRecord { BuildId = build.Id, View = view };
        record.InputArtifactId = source.Id;
        record.OutputArtifactId = uploaded.Id;
        record.Status = CharacterIdentityAngleStatus.Complete;
        record.ResolvedPromptText = null;
        record.ResolvedModelId = null;
        record.ManualConfirmationRequired = view is CharacterIdentityAngleView.ProfileLeft or CharacterIdentityAngleView.ProfileRight;
        record.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertAngleAsync(record, cancellationToken);
        var attempts = await _repository.ListAngleAttemptsAsync(record.Id, cancellationToken);
        await _repository.UpsertAngleAttemptAsync(new CharacterIdentityAngleAttempt
        {
            AngleId = record.Id,
            AttemptNumber = attempts.Count + 1,
            InputArtifactId = source.Id,
            OutputArtifactId = uploaded.Id,
            Status = CharacterIdentityAngleStatus.Complete
        }, cancellationToken);
        return record;
    }

    public async Task<CharacterIdentityAngleRecord> RecordResultAsync(
        string buildId,
        CharacterIdentityAngleView view,
        string outputArtifactId,
        string? promptText = null,
        CancellationToken cancellationToken = default)
    {
        var angles = (await _repository.ListAnglesAsync(buildId, cancellationToken)).ToList();
        var record = angles.FirstOrDefault(a => a.View == view)
            ?? throw new InvalidOperationException($"Angle '{view}' has not been prepared.");
        var existingAttempt = (await _repository.ListAngleAttemptsAsync(record.Id, cancellationToken))
            .FirstOrDefault(attempt => string.Equals(attempt.OutputArtifactId, outputArtifactId.Trim(), StringComparison.Ordinal));
        if (existingAttempt is not null)
        {
            existingAttempt.Status = CharacterIdentityAngleStatus.Complete;
            existingAttempt.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertAngleAttemptAsync(existingAttempt, cancellationToken);
            return record;
        }
        if (!string.IsNullOrWhiteSpace(promptText))
            record.ResolvedPromptText = promptText.Trim();
        var attempts = await _repository.ListAngleAttemptsAsync(record.Id, cancellationToken);
        await _repository.UpsertAngleAttemptAsync(new CharacterIdentityAngleAttempt
        {
            AngleId = record.Id,
            AttemptNumber = attempts.Count + 1,
            InputArtifactId = record.InputArtifactId ?? throw new InvalidOperationException($"Angle '{view}' has no input artifact."),
            OutputArtifactId = outputArtifactId.Trim(),
            PromptText = record.ResolvedPromptText,
            ResolvedModelId = record.ResolvedModelId,
            Status = CharacterIdentityAngleStatus.Complete
        }, cancellationToken);
        return record;
    }

    public async Task<CharacterIdentityAngleRecord> RunAsync(
        string buildId,
        CharacterIdentityAngleView view,
        string modelId,
        string? promptOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException("An exact image editor model is required for an angle.");

        var build = await _builds.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
        if (build.CurrentStep != CharacterIdentityBuildStep.Angles)
            throw new InvalidOperationException($"Face angles can only run at the Angles step; current step is {build.CurrentStep}.");

        var canonical = build.CanonicalFrontAssetId
            ?? throw new InvalidOperationException("Approve a canonical front before creating face angles.");
        var angles = (await _repository.ListAnglesAsync(build.Id, cancellationToken)).ToList();
        var sourceId = ResolveSource(view, canonical, angles);
        var key = PromptKey(view);
        var template = await _templates.ResolveAsync(key, build.CharacterProfileId, cancellationToken);
        var prompt = string.IsNullOrWhiteSpace(promptOverride) ? template.Body : promptOverride.Trim();
        var source = await _assets.GetImageAsync(sourceId, cancellationToken)
            ?? throw new InvalidOperationException($"Angle source image '{sourceId}' was not found.");
        var image = await _assets.EnqueueImageEditAsync(
            source.AssetId,
            sourceId,
            prompt,
            modelId,
            cancellationToken,
            candidateBatchId: CandidateBatchIdFor(build.Id, view));

        var record = angles.FirstOrDefault(a => a.View == view) ?? new CharacterIdentityAngleRecord { BuildId = build.Id, View = view };
        record.Status = CharacterIdentityAngleStatus.Pending;
        record.InputArtifactId = sourceId;
        record.OutputArtifactId = image.Id;
        record.ResolvedPromptText = prompt;
        record.ResolvedModelId = modelId.Trim();
        record.FailureReason = null;
        record.ManualConfirmationRequired = view is CharacterIdentityAngleView.ProfileLeft or CharacterIdentityAngleView.ProfileRight;
        record.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertAngleAsync(record, cancellationToken);
        var attempts = await _repository.ListAngleAttemptsAsync(record.Id, cancellationToken);
        await _repository.UpsertAngleAttemptAsync(new CharacterIdentityAngleAttempt
        {
            AngleId = record.Id,
            AttemptNumber = attempts.Count + 1,
            InputArtifactId = sourceId,
            OutputArtifactId = image.Id,
            PromptText = prompt,
            ResolvedModelId = modelId.Trim(),
            Status = CharacterIdentityAngleStatus.Pending
        }, cancellationToken);
        return record;
    }

    public async Task<CharacterIdentityAngleRecord> AcceptAsync(
        string buildId,
        CharacterIdentityAngleView view,
        bool manualConfirmed = false,
        CancellationToken cancellationToken = default)
    {
        var build = await _builds.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
        var angles = (await _repository.ListAnglesAsync(build.Id, cancellationToken)).ToList();
        var record = angles.FirstOrDefault(a => a.View == view)
            ?? throw new InvalidOperationException($"Angle '{view}' has not been run.");
        if (string.IsNullOrWhiteSpace(record.AcceptedAttemptId) || string.IsNullOrWhiteSpace(record.OutputArtifactId))
            throw new InvalidOperationException($"Angle '{view}' has no accepted attempt.");
        var image = await _assets.GetImageAsync(record.OutputArtifactId, cancellationToken)
            ?? throw new InvalidOperationException($"Angle output '{record.OutputArtifactId}' was not found.");
        if (image.Status != SceneAssetStatus.Complete)
            throw new InvalidOperationException($"Angle '{view}' is not complete yet.");
        if (record.ManualConfirmationRequired && !manualConfirmed && !record.ManualConfirmed)
            throw new InvalidOperationException($"Angle '{view}' requires explicit visual confirmation.");

        record.ManualConfirmed |= manualConfirmed;
        record.Status = CharacterIdentityAngleStatus.Accepted;
        record.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertAngleAsync(record, cancellationToken);

        var acceptedViews = (await _repository.ListAnglesAsync(build.Id, cancellationToken))
            .ToDictionary(a => a.View, a => a.Status);
        if (Ordered.All(required => acceptedViews.TryGetValue(required, out var status)
            && status == CharacterIdentityAngleStatus.Accepted))
        {
            var front = build.CanonicalFrontAssetId!;
            await _builds.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Angles, front, record.OutputArtifactId, cancellationToken: cancellationToken);
        }

        return record;
    }

    public async Task<CharacterIdentityAngleRecord> AcceptAttemptAsync(
        string buildId, CharacterIdentityAngleView view, string attemptId, bool manualConfirmed = false,
        CancellationToken cancellationToken = default)
    {
        var angles = (await _repository.ListAnglesAsync(buildId, cancellationToken)).ToList();
        var angle = angles.FirstOrDefault(a => a.View == view)
            ?? throw new InvalidOperationException($"Angle '{view}' has not been prepared.");
        var attempts = await _repository.ListAngleAttemptsAsync(angle.Id, cancellationToken);
        var attempt = attempts.FirstOrDefault(a => a.Id == attemptId)
            ?? throw new InvalidOperationException($"Angle attempt '{attemptId}' was not found.");
        var image = await _assets.GetImageAsync(attempt.OutputArtifactId, cancellationToken)
            ?? throw new InvalidOperationException($"Angle attempt image '{attempt.OutputArtifactId}' was not found.");
        if (image.Status != SceneAssetStatus.Complete)
            throw new InvalidOperationException($"Angle attempt {attempt.AttemptNumber} is not complete yet.");
        if (attempt.Status != CharacterIdentityAngleStatus.Complete && !attempt.ManualOverrideApplied)
            throw new InvalidOperationException($"Angle attempt {attempt.AttemptNumber} has not passed validation or received a manual override.");
        angle.OutputArtifactId = attempt.OutputArtifactId;
        angle.InputArtifactId = attempt.InputArtifactId;
        angle.AcceptedAttemptId = attempt.Id;
        angle.ResolvedPromptText = attempt.PromptText;
        angle.ResolvedModelId = attempt.ResolvedModelId;
        angle.Status = CharacterIdentityAngleStatus.Pending;
        angle.ManualConfirmed |= manualConfirmed;
        await _repository.UpsertAngleAsync(angle, cancellationToken);
        attempt.Status = CharacterIdentityAngleStatus.Accepted;
        attempt.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertAngleAttemptAsync(attempt, cancellationToken);
        angle.Status = CharacterIdentityAngleStatus.Accepted;
        await _repository.UpsertAngleAsync(angle, cancellationToken);
        var acceptedViews = (await _repository.ListAnglesAsync(buildId, cancellationToken))
            .ToDictionary(a => a.View, a => a.Status);
        if (Ordered.All(required => acceptedViews.TryGetValue(required, out var status)
            && status == CharacterIdentityAngleStatus.Accepted))
        {
            var build = await _builds.GetBuildAsync(buildId, cancellationToken)
                ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
            await _builds.CompleteStepAsync(
                buildId,
                CharacterIdentityBuildStep.Angles,
                build.CanonicalFrontAssetId,
                attempt.OutputArtifactId,
                cancellationToken: cancellationToken);
        }
        return angle;
    }

    public async Task DeleteAttemptAsync(
        string buildId, CharacterIdentityAngleView view, string attemptId,
        CancellationToken cancellationToken = default)
    {
        var angle = (await _repository.ListAnglesAsync(buildId, cancellationToken)).FirstOrDefault(a => a.View == view)
            ?? throw new InvalidOperationException($"Angle '{view}' has not been prepared.");
        var attempt = (await _repository.ListAngleAttemptsAsync(angle.Id, cancellationToken)).FirstOrDefault(a => a.Id == attemptId)
            ?? throw new InvalidOperationException($"Angle attempt '{attemptId}' was not found.");
        if (string.Equals(angle.OutputArtifactId, attempt.OutputArtifactId, StringComparison.Ordinal))
            throw new InvalidOperationException("The accepted angle cannot be deleted. Select another completed attempt first.");

        var image = await _assets.GetImageAsync(attempt.OutputArtifactId, cancellationToken);
        if (image is not null)
            await _assets.DeleteImageAsync(image.Id, cancellationToken);
        await _repository.DeleteAngleAttemptAsync(attempt.Id, cancellationToken);
    }

    public async Task RecordOverrideAsync(string buildId, CharacterIdentityAngleView view, string attemptId, string reason, string author, CancellationToken cancellationToken = default)
    {
        var angle = (await _repository.ListAnglesAsync(buildId, cancellationToken)).FirstOrDefault(a => a.View == view)
            ?? throw new InvalidOperationException($"Angle '{view}' has not been prepared.");
        var attempt = (await _repository.ListAngleAttemptsAsync(angle.Id, cancellationToken)).FirstOrDefault(a => a.Id == attemptId)
            ?? throw new InvalidOperationException($"Angle attempt '{attemptId}' was not found.");
        await _repository.RecordAngleAttemptOverrideAsync(attempt.Id, reason, author, cancellationToken);
    }

    private static string ResolveSource(CharacterIdentityAngleView view, string canonical, IReadOnlyList<CharacterIdentityAngleRecord> angles)
    {
        if (view is CharacterIdentityAngleView.ThreeQuarterLeft or CharacterIdentityAngleView.ThreeQuarterRight)
            return canonical;
        var prior = view == CharacterIdentityAngleView.ProfileLeft
            ? CharacterIdentityAngleView.ThreeQuarterLeft
            : CharacterIdentityAngleView.ThreeQuarterRight;
        var source = angles.FirstOrDefault(a => a.View == prior && a.Status == CharacterIdentityAngleStatus.Accepted)?.OutputArtifactId;
        return !string.IsNullOrWhiteSpace(source)
            ? source
            : throw new InvalidOperationException($"Accept the {prior} angle before creating {view}.");
    }

    private static string PromptKey(CharacterIdentityAngleView view) => view switch
    {
        CharacterIdentityAngleView.ThreeQuarterLeft => "identity.angle.three-quarter",
        CharacterIdentityAngleView.ThreeQuarterRight => "identity.angle.three-quarter.right",
        CharacterIdentityAngleView.ProfileLeft => "identity.angle.profile",
        CharacterIdentityAngleView.ProfileRight => "identity.angle.profile.right",
        _ => throw new InvalidOperationException($"Unsupported angle view '{view}'.")
    };

}