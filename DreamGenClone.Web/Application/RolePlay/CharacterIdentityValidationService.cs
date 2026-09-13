using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Validate-step orchestration. Settings are resolved from the <b>global</b>
/// <see cref="ReferenceWorkflowSettings"/> row: <c>seed-prompts.md</c> §2 declares
/// <c>EyeToolPythonPath</c> and <c>EyeGateMaxAbsIrisDyPercent</c> global-scope, and a per-character
/// row (created when the user picks a front model) must not shadow them.
/// </summary>
public sealed class CharacterIdentityValidationService : ICharacterIdentityValidationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICharacterIdentityBuildService _builds;
    private readonly ICharacterIdentityBuildRepository _repository;
    private readonly IImageWorkflowTemplateService _templates;
    private readonly ISceneAssetService _assets;
    private readonly ICharacterIdentityMeasurementService _measurements;
    private readonly PersistenceOptions _persistence;
    private readonly ILogger<CharacterIdentityValidationService> _logger;

    public CharacterIdentityValidationService(
        ICharacterIdentityBuildService builds,
        ICharacterIdentityBuildRepository repository,
        IImageWorkflowTemplateService templates,
        ISceneAssetService assets,
        ICharacterIdentityMeasurementService measurements,
        IOptions<PersistenceOptions> persistence,
        ILogger<CharacterIdentityValidationService> logger)
    {
        _builds = builds;
        _repository = repository;
        _templates = templates;
        _assets = assets;
        _measurements = measurements;
        _persistence = persistence.Value;
        _logger = logger;
    }

    public async Task<CharacterIdentityValidationResult> GetGateAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var (build, steps) = await LoadAsync(buildId, cancellationToken);
        var settings = await ResolveSettingsAsync(cancellationToken);
        return BuildResult(build, steps, settings.EyeGateMaxAbsIrisDyPercent);
    }

    public async Task<CharacterIdentityValidationResult> MeasureAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var (build, steps) = await LoadAsync(buildId, cancellationToken);
        var frontArtifactId = RequireFrontArtifactId(steps);
        var image = await _assets.GetImageAsync(frontArtifactId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The validated front image '{frontArtifactId}' was not found in the asset library.");
        var imagePath = ResolveImagePath(image);

        // The tool invocation, interpreter resolution and parsing live in the ONE measurement service, so
        // the crop step and this step can never drift apart on how the tool is run or read.
        var run = await _measurements.MeasureFileAsync(imagePath, cancellationToken);

        var settings = await ResolveSettingsAsync(cancellationToken);

        var validateRow = RequireRow(steps, CharacterIdentityBuildStep.Validate);
        validateRow.MeasurementJson = JsonSerializer.Serialize(run.Measurement, JsonOptions);
        validateRow.RawToolOutput = run.RawOutput;
        validateRow.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertStepAsync(validateRow, cancellationToken);

        var refreshed = await _repository.ListStepsAsync(build.Id, cancellationToken);
        var result = BuildResult(build, refreshed, settings.EyeGateMaxAbsIrisDyPercent);
        _logger.LogInformation(
            "Eye validation verdict: BuildId={BuildId}, Verdict={Verdict}, IrisDy%={IrisDy}",
            build.Id, result.Verdict, result.Measurement?.IrisDyPercent);
        return result;
    }

    public async Task<SceneAssetImageValidation> MeasureImageAsync(
        string imageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageId))
        {
            throw new InvalidOperationException("An image id is required to validate an image.");
        }

        var image = await _assets.GetImageAsync(imageId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException(
                $"Image '{imageId}' was not found in the asset library, so it cannot be validated.");

        var settings = await ResolveSettingsAsync(cancellationToken);
        var run = await _measurements.MeasureFileAsync(ResolveImagePath(image), cancellationToken);
        var thresholdPercent = settings.EyeGateMaxAbsIrisDyPercent;
        var verdict = Classify(run.Measurement, thresholdPercent);

        var result = new SceneAssetImageValidation(
            verdict,
            run.Measurement.IrisDyPercent,
            run.Measurement.EyeDyPercent,
            run.Measurement.InterocularPixels,
            run.Measurement.Head?.HeadHeightPx,
            thresholdPercent,
            DescribeBlock(verdict, run.Measurement, thresholdPercent),
            run.RawOutput,
            DateTime.UtcNow);

        await _assets.SetImageValidationResultAsync(
            image.Id, JsonSerializer.Serialize(result, JsonOptions), cancellationToken);

        // A failed gate takes the image out of contention. Nothing is inferred: the tool's own verdict is
        // the reason, and the record above keeps the measurements that produced it.
        if (verdict is CharacterIdentityValidationVerdict.Fail or CharacterIdentityValidationVerdict.NoFaceMesh)
        {
            await _assets.SetImageCandidateDecisionAsync(
                image.Id, SceneAssetCandidateDecision.Rejected, result.BlockReason, cancellationToken);
        }

        _logger.LogInformation(
            "Image eye validation: ImageId={ImageId}, Verdict={Verdict}, IrisDy%={IrisDy}, Threshold={Threshold}",
            image.Id, verdict, result.IrisDyPercent, thresholdPercent);

        return result;
    }

    public async Task<CharacterIdentityValidationResult> RecordOverrideAsync(
        string buildId, string reason, string author, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("A reason is required to record a validation override.");
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            throw new InvalidOperationException("An author is required to record a validation override.");
        }

        var (build, steps) = await LoadAsync(buildId, cancellationToken);
        RequireFrontArtifactId(steps);

        var row = RequireRow(steps, CharacterIdentityBuildStep.Validate);
        row.ManualOverrideApplied = true;
        row.ManualOverrideReason = reason.Trim();
        row.ManualOverrideAuthor = author.Trim();
        row.ManualOverrideUtc = DateTime.UtcNow;
        row.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertStepAsync(row, cancellationToken);

        _logger.LogInformation(
            "Validation override recorded: BuildId={BuildId}, Author={Author}", build.Id, row.ManualOverrideAuthor);

        var refreshed = await _repository.ListStepsAsync(build.Id, cancellationToken);
        var settings = await ResolveSettingsAsync(cancellationToken);
        return BuildResult(build, refreshed, settings.EyeGateMaxAbsIrisDyPercent);
    }

    public async Task<CharacterIdentityValidationResult> AdvanceAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var (build, steps) = await LoadAsync(buildId, cancellationToken);
        var frontArtifactId = RequireFrontArtifactId(steps);
        var settings = await ResolveSettingsAsync(cancellationToken);
        var gate = BuildResult(build, steps, settings.EyeGateMaxAbsIrisDyPercent);

        if (!gate.CanAdvance)
        {
            throw new InvalidOperationException(
                $"The pipeline cannot advance past Validate: {gate.BlockReason}");
        }

        await _builds.CompleteStepAsync(
            build.Id,
            CharacterIdentityBuildStep.Validate,
            inputArtifactId: frontArtifactId,
            outputArtifactId: frontArtifactId,
            cancellationToken: cancellationToken);

        var refreshed = await _repository.ListStepsAsync(build.Id, cancellationToken);
        var updated = await _repository.GetBuildAsync(build.Id, cancellationToken) ?? build;
        return BuildResult(updated, refreshed, settings.EyeGateMaxAbsIrisDyPercent);
    }

    private async Task<ReferenceWorkflowSettings> ResolveSettingsAsync(CancellationToken cancellationToken)
        => await _templates.ResolveSettingsAsync(null, cancellationToken);

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

    private static string RequireFrontArtifactId(IReadOnlyList<CharacterIdentityBuildStepRecord> steps)
    {
        var front = RequireRow(steps, CharacterIdentityBuildStep.Front);
        if (front.Status != CharacterIdentityBuildStepStatus.Complete)
        {
            throw new InvalidOperationException(
                "The Validate step requires a completed Front step. Select a front image first.");
        }

        if (string.IsNullOrWhiteSpace(front.OutputArtifactId))
        {
            throw new InvalidOperationException("The completed Front step has no output artifact to validate.");
        }

        return front.OutputArtifactId.Trim();
    }

    private static CharacterIdentityBuildStepRecord RequireRow(
        IReadOnlyList<CharacterIdentityBuildStepRecord> steps, CharacterIdentityBuildStep step)
        => steps.FirstOrDefault(row => row.Step == step)
            ?? throw new InvalidOperationException($"The build has no recorded row for step '{step}'.");

    private string ResolveImagePath(SceneAssetImage image)
    {
        if (string.IsNullOrWhiteSpace(image.FileRelativePath))
        {
            throw new InvalidOperationException(
                $"The front image '{image.Id}' has no stored file to validate.");
        }

        var absolute = Path.GetFullPath(Path.Combine(_persistence.SceneImageRoot, image.FileRelativePath));
        if (!File.Exists(absolute))
        {
            throw new InvalidOperationException($"The front image file was not found at '{absolute}'.");
        }

        return absolute;
    }

    private static CharacterIdentityValidationResult BuildResult(
        CharacterIdentityBuild build,
        IReadOnlyList<CharacterIdentityBuildStepRecord> steps,
        double thresholdPercent)
    {
        var front = steps.FirstOrDefault(row => row.Step == CharacterIdentityBuildStep.Front);
        var validate = steps.FirstOrDefault(row => row.Step == CharacterIdentityBuildStep.Validate);
        var measurement = DeserializeMeasurement(validate?.MeasurementJson);

        var verdict = Classify(measurement, thresholdPercent);
        var overrideApplied = validate?.ManualOverrideApplied == true;

        var canAdvance = verdict switch
        {
            CharacterIdentityValidationVerdict.Pass => true,
            CharacterIdentityValidationVerdict.Fail or CharacterIdentityValidationVerdict.NoFaceMesh => overrideApplied,
            _ => false
        };

        var blockReason = canAdvance ? null : DescribeBlock(verdict, measurement, thresholdPercent);

        return new CharacterIdentityValidationResult
        {
            BuildId = build.Id,
            FrontArtifactId = front?.OutputArtifactId,
            Verdict = verdict,
            Measurement = measurement,
            ThresholdPercent = thresholdPercent,
            ManualOverrideApplied = overrideApplied,
            ManualOverrideReason = validate?.ManualOverrideReason,
            ManualOverrideAuthor = validate?.ManualOverrideAuthor,
            ManualOverrideUtc = validate?.ManualOverrideUtc,
            CanAdvance = canAdvance,
            BlockReason = blockReason
        };
    }

    private static CharacterIdentityValidationVerdict Classify(
        CharacterIdentityEyeMeasurement? measurement, double thresholdPercent)
    {
        if (measurement is null)
        {
            return CharacterIdentityValidationVerdict.NotRun;
        }

        if (!string.IsNullOrWhiteSpace(measurement.Error))
        {
            return CharacterIdentityValidationVerdict.NoFaceMesh;
        }

        if (measurement.IrisDyPercent is not { } irisDyPercent)
        {
            return CharacterIdentityValidationVerdict.NotRun;
        }

        return Math.Abs(irisDyPercent) <= thresholdPercent
            ? CharacterIdentityValidationVerdict.Pass
            : CharacterIdentityValidationVerdict.Fail;
    }

    private static string? DescribeBlock(
        CharacterIdentityValidationVerdict verdict,
        CharacterIdentityEyeMeasurement? measurement,
        double thresholdPercent)
        => verdict switch
        {
            CharacterIdentityValidationVerdict.NotRun => "Run validation to continue.",
            CharacterIdentityValidationVerdict.Fail =>
                $"Eye gate failed: |irisDy%| {Math.Abs(measurement?.IrisDyPercent ?? 0):F2} exceeds the configured "
                + $"{thresholdPercent:F2}. Record a manual override with a reason to continue.",
            CharacterIdentityValidationVerdict.NoFaceMesh =>
                "The eye tool returned no face mesh (expected for a full profile). "
                + "Record a manual override with a reason to continue.",
            _ => null
        };

    private static CharacterIdentityEyeMeasurement? DeserializeMeasurement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CharacterIdentityEyeMeasurement>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
