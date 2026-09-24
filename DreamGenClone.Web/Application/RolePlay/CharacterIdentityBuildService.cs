using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class CharacterIdentityBuildService : ICharacterIdentityBuildService
{
    private readonly ICharacterIdentityBuildRepository _repository;
    private readonly ISceneAssetService _assets;
    private readonly ICharacterIdentityStepPlanService _plans;
    private readonly ILogger<CharacterIdentityBuildService> _logger;

    /// <summary>
    /// The three pipeline steps that turn the chosen front candidate into the canonical front, in order,
    /// with the operation that has to appear in an image's lineage for that step to count as done.
    /// </summary>
    private static readonly (CharacterIdentityBuildStep Step, MediaEditOperationKind Operation)[] FrontPipelineOperations =
    [
        (CharacterIdentityBuildStep.GarmentRemoval, MediaEditOperationKind.Edit),
        (CharacterIdentityBuildStep.Crop, MediaEditOperationKind.Crop),
        (CharacterIdentityBuildStep.Enhance, MediaEditOperationKind.Enhance)
    ];

    /// <summary>
    /// The per-image pipeline record is read by people as well as code ("was this image de-clothed?"), so
    /// its enums are written as names rather than numbers.
    /// </summary>
    private static readonly JsonSerializerOptions PipelineJsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public CharacterIdentityBuildService(
        ICharacterIdentityBuildRepository repository,
        ISceneAssetService assets,
        ICharacterIdentityStepPlanService plans,
        ILogger<CharacterIdentityBuildService> logger)
    {
        _repository = repository;
        _assets = assets;
        _plans = plans;
        _logger = logger;
    }

    public async Task<CharacterIdentityBuild> CreateBuildAsync(
        string characterProfileId,
        string? batchId,
        CharacterIdentityTargetKind targetKind,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterProfileId))
            throw new InvalidOperationException("A character profile id is required to start a build.");

        // The steps come from the target kind's plan, so a new target kind starts here with no change to this
        // method: the rows are whatever the plan names, in the plan's order.
        var plan = await _plans.GetPlanAsync(targetKind, cancellationToken);
        var build = new CharacterIdentityBuild
        {
            CharacterTemplateId = characterProfileId.Trim(),
            BatchId = string.IsNullOrWhiteSpace(batchId) ? null : batchId.Trim(),
            TargetKind = targetKind,
            CurrentStep = plan.Ordered[0].Step,
            Status = CharacterIdentityBuildStatus.InProgress
        };
        await _repository.UpsertBuildAsync(build, cancellationToken);

        foreach (var definition in plan.Ordered)
        {
            await _repository.UpsertStepAsync(new CharacterIdentityBuildStepRecord
            {
                BuildId = build.Id,
                Step = definition.Step,
                Status = CharacterIdentityBuildStepStatus.NotStarted
            }, cancellationToken);
        }

        return build;
    }

    public Task<CharacterIdentityBuild?> GetBuildAsync(string buildId, CancellationToken cancellationToken = default)
        => _repository.GetBuildAsync(buildId, cancellationToken);

    public Task<IReadOnlyList<CharacterIdentityBuild>> ListBuildsAsync(
        string characterProfileId, CancellationToken cancellationToken = default)
        => _repository.ListBuildsAsync(characterProfileId, cancellationToken);

    public Task<IReadOnlyList<CharacterIdentityBuildStepRecord>> ListStepsAsync(
        string buildId, CancellationToken cancellationToken = default)
        => _repository.ListStepsAsync(buildId, cancellationToken);

    public async Task<CharacterIdentityBuild> CompleteStepAsync(
        string buildId,
        CharacterIdentityBuildStep step,
        string? inputArtifactId,
        string outputArtifactId,
        string? resolvedPromptText = null,
        string? resolvedModelId = null,
        bool mirrorDerived = false,
        CancellationToken cancellationToken = default)
    {
        var (build, steps) = await LoadAsync(buildId, cancellationToken);
        var plan = await _plans.GetPlanAsync(build.TargetKind, cancellationToken);
        EnsureInProgress(build);
        EnsureCurrent(plan, steps, step);

        if (step != plan.Ordered[0].Step && string.IsNullOrWhiteSpace(inputArtifactId))
            throw new InvalidOperationException($"Step {step} requires an input artifact.");
        if (string.IsNullOrWhiteSpace(outputArtifactId))
            throw new InvalidOperationException($"Step {step} requires an output artifact.");

        var row = RequireRow(steps, step);
        row.Status = CharacterIdentityBuildStepStatus.Complete;
        row.InputArtifactId = string.IsNullOrWhiteSpace(inputArtifactId) ? null : inputArtifactId.Trim();
        row.OutputArtifactId = outputArtifactId.Trim();
        row.ResolvedPromptText = resolvedPromptText;
        row.ResolvedModelId = resolvedModelId;
        row.MirrorDerived = mirrorDerived;
        row.FailureReason = null;
        row.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertStepAsync(row, cancellationToken);

        build.CurrentStep = FirstIncomplete(plan, steps);
        build.Status = AllDone(plan, steps) ? CharacterIdentityBuildStatus.Complete : CharacterIdentityBuildStatus.InProgress;
        build.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBuildAsync(build, cancellationToken);
        return build;
    }

    public async Task<CharacterIdentityBuild> FailStepAsync(
        string buildId, CharacterIdentityBuildStep step, string failureReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
            throw new InvalidOperationException("A failure reason is required.");

        var (build, steps) = await LoadAsync(buildId, cancellationToken);
        var plan = await _plans.GetPlanAsync(build.TargetKind, cancellationToken);
        EnsureInProgress(build);
        EnsureCurrent(plan, steps, step);

        var row = RequireRow(steps, step);
        row.Status = CharacterIdentityBuildStepStatus.Failed;
        row.FailureReason = failureReason.Trim();
        row.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertStepAsync(row, cancellationToken);

        build.Status = CharacterIdentityBuildStatus.InProgress;
        build.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBuildAsync(build, cancellationToken);
        return build;
    }

    public async Task<CharacterIdentityBuild> SkipStepAsync(
        string buildId, CharacterIdentityBuildStep step, CancellationToken cancellationToken = default)
    {
        var (build, steps) = await LoadAsync(buildId, cancellationToken);
        var plan = await _plans.GetPlanAsync(build.TargetKind, cancellationToken);
        EnsureInProgress(build);
        EnsureCurrent(plan, steps, step);

        var row = RequireRow(steps, step);
        row.Status = CharacterIdentityBuildStepStatus.Skipped;
        row.FailureReason = null;
        row.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertStepAsync(row, cancellationToken);

        build.CurrentStep = FirstIncomplete(plan, steps);
        build.Status = AllDone(plan, steps) ? CharacterIdentityBuildStatus.Complete : CharacterIdentityBuildStatus.InProgress;
        build.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBuildAsync(build, cancellationToken);
        return build;
    }

    public async Task<CharacterIdentityBuild> ReRunStepAsync(
        string buildId, CharacterIdentityBuildStep step, CancellationToken cancellationToken = default)
    {
        var (build, steps) = await LoadAsync(buildId, cancellationToken);
        var plan = await _plans.GetPlanAsync(build.TargetKind, cancellationToken);
        var definition = plan.Require(step);

        var now = DateTime.UtcNow;
        foreach (var row in steps.Where(s => plan.Require(s.Step).Order >= definition.Order))
        {
            row.Status = CharacterIdentityBuildStepStatus.NotStarted;
            row.InputArtifactId = null;
            row.OutputArtifactId = null;
            row.ResolvedPromptText = null;
            row.ResolvedModelId = null;
            row.FailureReason = null;
            row.MirrorDerived = false;
            // A manual override is a decision about the artifact this step was validated against. Resetting
            // the step withdraws that artifact, so the recorded decision goes with it: a newly chosen front
            // must not inherit an override that was written about a different image.
            row.ManualOverrideApplied = false;
            row.ManualOverrideReason = null;
            row.ManualOverrideAuthor = null;
            row.ManualOverrideUtc = null;
            row.UpdatedUtc = now;
            await _repository.UpsertStepAsync(row, cancellationToken);
        }

        build.CurrentStep = step;
        build.Status = CharacterIdentityBuildStatus.InProgress;
        build.UpdatedUtc = now;
        await _repository.UpsertBuildAsync(build, cancellationToken);
        return build;
    }

    public async Task<CharacterIdentityBuild> SetFrontContainerAsync(
        string buildId, string frontContainerAssetId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(frontContainerAssetId))
            throw new InvalidOperationException("A front container asset id is required.");

        var build = await _repository.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
        build.FrontContainerAssetId = frontContainerAssetId.Trim();
        build.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBuildAsync(build, cancellationToken);
        return build;
    }

    /// <summary>
    /// Records the approved canonical front for this build and finishes the de-clothe / crop / enhance part
    /// of the pipeline from what that image actually went through (B-121 notes 002 and 005).
    ///
    /// The approved image's own lineage is the evidence: walking <c>SourceImageId</c> back to the Front
    /// step's output yields the de-clothe, crop and enhance artifacts this image was built from. Each of the
    /// three steps is then written as <c>Complete</c> — naming the artifact that operation contributed to
    /// this chain — or as <c>Skipped</c>, meaning this image never went through it. The summary is stored on
    /// the image itself, and the build moves on to Angles.
    ///
    /// Nothing is inferred. An image whose origin cannot be read, or whose lineage never reaches the Front
    /// step's output, is refused rather than assumed to have been de-clothed.
    /// </summary>
    public async Task<CharacterIdentityBuild> SetCanonicalFrontAsync(
        string buildId, string imageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageId))
            throw new InvalidOperationException("An image id is required to approve a canonical front.");

        var (build, steps) = await LoadAsync(buildId, cancellationToken);
        var plan = await _plans.GetPlanAsync(build.TargetKind, cancellationToken);
        if (string.IsNullOrWhiteSpace(build.FrontContainerAssetId))
            throw new InvalidOperationException("This build has no front container yet.");

        var frontRow = RequireRow(steps, plan.Ordered[0].Step);
        if (frontRow.Status != CharacterIdentityBuildStepStatus.Complete
            || string.IsNullOrWhiteSpace(frontRow.OutputArtifactId))
        {
            throw new InvalidOperationException(
                $"The {plan.Ordered[0].Step} step must be complete before a canonical front can be approved.");
        }

        var frontArtifactId = frontRow.OutputArtifactId.Trim();
        var image = await _assets.GetImageAsync(imageId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Image '{imageId}' was not found in the asset library.");
        if (!string.Equals(image.AssetId, build.FrontContainerAssetId, StringComparison.Ordinal))
            throw new InvalidOperationException("The approved image does not belong to this build's front asset.");
        if (image.Status != SceneAssetStatus.Complete)
            throw new InvalidOperationException("The approved image is not complete yet.");

        var chain = await ResolveLineageAsync(image, frontArtifactId, build.FrontContainerAssetId, cancellationToken);
        var pipeline = DeriveFrontPipeline(frontArtifactId, chain);

        var now = DateTime.UtcNow;
        foreach (var pipelineStep in pipeline.Steps)
        {
            var row = RequireRow(steps, pipelineStep.Step);
            row.Status = pipelineStep.Outcome;
            row.InputArtifactId = pipelineStep.InputArtifactId;
            row.OutputArtifactId = pipelineStep.ArtifactId;
            row.FailureReason = null;
            row.UpdatedUtc = now;
            await _repository.UpsertStepAsync(row, cancellationToken);
        }

        await _assets.SetImagePipelineStepsAsync(
            image.Id,
            JsonSerializer.Serialize(pipeline, PipelineJsonOptions),
            cancellationToken);

        build.CanonicalFrontAssetId = image.Id;
        build.CurrentStep = FirstIncomplete(plan, steps);
        build.Status = AllDone(plan, steps)
            ? CharacterIdentityBuildStatus.Complete
            : CharacterIdentityBuildStatus.InProgress;
        build.UpdatedUtc = now;
        await _repository.UpsertBuildAsync(build, cancellationToken);

        _logger.LogInformation(
            "Canonical front approved: BuildId={BuildId}, ImageId={ImageId}, DeClothe={DeClothe}, Crop={Crop}, Enhance={Enhance}, CurrentStep={Step}",
            build.Id,
            image.Id,
            pipeline.Steps[0].Outcome,
            pipeline.Steps[1].Outcome,
            pipeline.Steps[2].Outcome,
            build.CurrentStep);

        return build;
    }

    /// <summary>
    /// Walks an image's lineage back to the Front step's output. Each derived image records, in its own
    /// provenance, the checksum of the exact file it was produced from; that checksum is resolved against the
    /// front container's images, so the chain survives a lost <c>SourceImageId</c> link. Where several images
    /// share that checksum (the same bytes can be produced twice), the image's own recorded
    /// <c>SourceImageId</c> picks between them, and an unresolvable step is refused rather than guessed.
    ///
    /// The result is oldest-first and excludes the Front artifact itself, so every member is an image an
    /// operation produced.
    /// </summary>
    private async Task<List<SceneAssetImage>> ResolveLineageAsync(
        SceneAssetImage image,
        string frontArtifactId,
        string frontContainerAssetId,
        CancellationToken cancellationToken)
    {
        var container = await _assets.ListImagesAsync(frontContainerAssetId, cancellationToken);
        var byId = container.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal);
        var bySha = container
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Sha256))
            .GroupBy(candidate => candidate.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var chain = new List<SceneAssetImage>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { image.Id };
        var current = image;
        while (!string.Equals(current.Id, frontArtifactId, StringComparison.Ordinal))
        {
            chain.Add(current);

            // A row that is not an operation result cannot be mid-chain: the chain starts at the Front
            // step's output, which is a generated or uploaded candidate.
            if (current.Kind != SceneAssetKind.Edited)
            {
                throw new InvalidOperationException(
                    $"Image '{image.Id}' does not derive from the Front step's output '{frontArtifactId}', "
                    + "so the pipeline steps it went through cannot be derived.");
            }

            var operation = MediaEditProvenance.RequireOperation(current.SourceProvenanceJson, current.Id);
            var source = ResolveSourceImage(bySha, current, operation.SourceImageSha256, frontContainerAssetId);
            if (!visited.Add(source.Id))
            {
                throw new InvalidOperationException(
                    $"Image '{image.Id}' has a circular lineage at '{source.Id}'.");
            }

            current = source;
        }

        chain.Reverse();
        return chain;    }

    /// <summary>
    /// Resolves the image an operation consumed from its recorded checksum. Ambiguity is resolved by the
    /// image's own <c>SourceImageId</c> when exactly one candidate carries it; anything else is refused,
    /// because picking one of several identical files would invent a lineage.
    /// </summary>
    private static SceneAssetImage ResolveSourceImage(
        IReadOnlyDictionary<string, List<SceneAssetImage>> bySha,
        SceneAssetImage image,
        string sourceImageSha256,
        string frontContainerAssetId)
    {
        if (!bySha.TryGetValue(sourceImageSha256, out var candidates) || candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"The image '{image.Id}' was produced from a file with checksum '{sourceImageSha256}', which is "
                + $"not an image of front asset '{frontContainerAssetId}'.");
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var named = string.IsNullOrWhiteSpace(image.SourceImageId)
            ? []
            : candidates.Where(candidate => string.Equals(candidate.Id, image.SourceImageId, StringComparison.Ordinal)).ToList();
        if (named.Count == 1)
        {
            return named[0];
        }

        throw new InvalidOperationException(
            $"Image '{image.Id}' was produced from a file that {candidates.Count} images of front asset "
            + $"'{frontContainerAssetId}' share ('{sourceImageSha256}'), and none of them is the source it "
            + "records, so its lineage cannot be determined.");
    }

    /// <summary>
    /// Turns a lineage into one outcome per pipeline step. Each step takes the artifact of its own operation
    /// that lies nearest the approved image (so a second crop supersedes the first), and a step whose
    /// operation is absent from the lineage is recorded as skipped. The step's input is the lineage member
    /// just older than its artifact — or the Front artifact when its artifact is the oldest member.
    /// </summary>
    private static SceneAssetImagePipeline DeriveFrontPipeline(
        string frontArtifactId, IReadOnlyList<SceneAssetImage> chain)
    {
        var steps = new List<SceneAssetImagePipelineStep>(FrontPipelineOperations.Length);
        string? previousOutput = null;
        foreach (var (step, kind) in FrontPipelineOperations)
        {
            var index = LastIndexOfOperationKind(chain, kind);
            if (index < 0)
            {
                var input = previousOutput ?? frontArtifactId;
                steps.Add(new SceneAssetImagePipelineStep(
                    step, CharacterIdentityBuildStepStatus.Skipped, input, null));
                previousOutput = input;
                continue;
            }

            var artifactId = chain[index].Id;
            var inputArtifactId = index == 0 ? frontArtifactId : chain[index - 1].Id;
            steps.Add(new SceneAssetImagePipelineStep(
                step, CharacterIdentityBuildStepStatus.Complete, inputArtifactId, artifactId));
            previousOutput = artifactId;
        }

        return new SceneAssetImagePipeline(frontArtifactId, steps, DateTime.UtcNow);
    }

    private static int LastIndexOfOperationKind(
        IReadOnlyList<SceneAssetImage> chain, MediaEditOperationKind kind)
    {
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            if (MediaEditProvenance.RequireOperation(chain[index].SourceProvenanceJson, chain[index].Id).Kind == kind)
            {
                return index;
            }
        }

        return -1;
    }

    private async Task<(CharacterIdentityBuild Build, List<CharacterIdentityBuildStepRecord> Steps)> LoadAsync(
        string buildId, CancellationToken cancellationToken)
    {        if (string.IsNullOrWhiteSpace(buildId))
            throw new InvalidOperationException("A build id is required.");

        var build = await _repository.GetBuildAsync(buildId, cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
        var steps = (await _repository.ListStepsAsync(buildId, cancellationToken)).ToList();
        return (build, steps);
    }

    private static void EnsureInProgress(CharacterIdentityBuild build)
    {
        if (build.Status == CharacterIdentityBuildStatus.Complete)
            throw new InvalidOperationException($"Build '{build.Id}' is already complete.");
    }

    private static void EnsureCurrent(
        CharacterIdentityStepPlan plan,
        IReadOnlyList<CharacterIdentityBuildStepRecord> steps,
        CharacterIdentityBuildStep step)
    {
        var current = FirstIncomplete(plan, steps);
        if (step != current)
            throw new InvalidOperationException($"Step {step} is out of order; the current step is {current}.");
    }

    private static CharacterIdentityBuildStepRecord RequireRow(
        IReadOnlyList<CharacterIdentityBuildStepRecord> steps, CharacterIdentityBuildStep step)
        => steps.FirstOrDefault(s => s.Step == step)
            ?? throw new InvalidOperationException($"Step {step} has no step record on this build.");

    private static CharacterIdentityBuildStep FirstIncomplete(
        CharacterIdentityStepPlan plan, IReadOnlyList<CharacterIdentityBuildStepRecord> steps)
    {
        foreach (var definition in plan.Ordered)
        {
            var row = steps.FirstOrDefault(s => s.Step == definition.Step);
            if (row is null
                || row.Status is CharacterIdentityBuildStepStatus.NotStarted
                    or CharacterIdentityBuildStepStatus.Running
                    or CharacterIdentityBuildStepStatus.Failed)
            {
                return definition.Step;
            }
        }

        // Every step is done: the build sits on its own terminal step, whatever the plan names as last.
        return plan.TerminalStep;
    }

    private static bool AllDone(
        CharacterIdentityStepPlan plan, IReadOnlyList<CharacterIdentityBuildStepRecord> steps)
        => plan.Ordered.All(definition =>
            steps.FirstOrDefault(s => s.Step == definition.Step)?.Status is
                CharacterIdentityBuildStepStatus.Complete or CharacterIdentityBuildStepStatus.Skipped);
}
