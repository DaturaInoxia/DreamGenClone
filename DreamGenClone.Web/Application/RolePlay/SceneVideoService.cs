using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Owns the Video Composer's lifecycle: seed resolution for the origin routes, composition persistence, and the
/// staged/queued/start/cancel transitions over the durable video lane (B-152, R2/R3/R4).
/// </summary>
/// <remarks>
/// The record is created on the first Stage or Start, never on a page visit, so an abandoned visit leaves no junk
/// rows. The durable job id is generated before the insert and stored on the record, so the composer can drive the
/// job (start, cancel) from the record alone.
/// </remarks>
public sealed class SceneVideoService : ISceneVideoService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneVideoRepository _repository;
    private readonly ISceneImageRepository _sceneImages;
    private readonly ISceneImageStorageService _imageStorage;
    private readonly ISceneVideoStorageService _videoStorage;
    private readonly IVideoAudioProcessor _audioProcessor;
    private readonly IModelResolutionService _modelResolution;
    private readonly SceneVideoChainPolicy _chainPolicy;
    private readonly ISceneAssetService _assets;
    private readonly ISceneBeatProductionPlanRepository _productionPlans;
    private readonly IDurableBackgroundJobQueue _durableQueue;
    private readonly ILogger<SceneVideoService> _logger;

    public SceneVideoService(
        ISceneVideoRepository repository,
        ISceneImageRepository sceneImages,
        ISceneImageStorageService imageStorage,
        ISceneVideoStorageService videoStorage,
        IVideoAudioProcessor audioProcessor,
        IModelResolutionService modelResolution,
        SceneVideoChainPolicy chainPolicy,
        ISceneAssetService assets,
        ISceneBeatProductionPlanRepository productionPlans,
        IDurableBackgroundJobQueue durableQueue,
        ILogger<SceneVideoService> logger)
    {
        _repository = repository;
        _sceneImages = sceneImages;
        _imageStorage = imageStorage;
        _videoStorage = videoStorage;
        _audioProcessor = audioProcessor;
        _modelResolution = modelResolution;
        _chainPolicy = chainPolicy;
        _assets = assets;
        _productionPlans = productionPlans;
        _durableQueue = durableQueue;
        _logger = logger;
    }

    public async Task<SceneVideoSeed?> ResolveSeedAsync(
        SceneVideoOriginKind originKind,
        string? originId,
        CancellationToken cancellationToken = default)
    {
        switch (originKind)
        {
            case SceneVideoOriginKind.Standalone:
                return null;

            case SceneVideoOriginKind.SceneImage:
            {
                var imageId = RequireOriginId(originId, "scene image");
                var image = await _sceneImages.GetImageAsync(imageId, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Scene image '{imageId}' was not found, so the Video Composer has no reference to open on.");

                if (string.IsNullOrWhiteSpace(image.FileRelativePath))
                {
                    throw new InvalidOperationException(
                        $"Scene image '{imageId}' has no stored file, so it cannot seed a video composition.");
                }

                return new SceneVideoSeed(
                    SceneVideoOriginKind.SceneImage,
                    image.Id,
                    image.FileRelativePath,
                    Path.GetFileName(image.FileRelativePath),
                    string.IsNullOrWhiteSpace(image.BeatId) ? null : $"Video from {image.BeatId}",
                    CoverageCameraIntent: null,
                    CoverageMotionIntent: null,
                    CoverageDurationHint: null,
                    SessionId: image.SessionId,
                    InteractionId: image.InteractionId);
            }

            case SceneVideoOriginKind.AssetImage:
            {
                var imageId = RequireOriginId(originId, "asset image");
                var image = await _assets.GetImageAsync(imageId, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Asset image '{imageId}' was not found, so the Video Composer has no reference to open on.");

                if (string.IsNullOrWhiteSpace(image.FileRelativePath))
                {
                    throw new InvalidOperationException(
                        $"Asset image '{imageId}' has no stored file, so it cannot seed a video composition.");
                }

                return new SceneVideoSeed(
                    SceneVideoOriginKind.AssetImage,
                    image.Id,
                    image.FileRelativePath,
                    Path.GetFileName(image.FileRelativePath),
                    string.IsNullOrWhiteSpace(image.DisplayName) ? null : image.DisplayName,
                    CoverageCameraIntent: null,
                    CoverageMotionIntent: null,
                    CoverageDurationHint: null,
                    SessionId: null,
                    InteractionId: null);
            }

            case SceneVideoOriginKind.VideoCoveragePlan:
            {
                var coveragePlanId = RequireOriginId(originId, "video coverage plan");
                var plan = await _productionPlans.GetByCoveragePlanIdAsync(coveragePlanId, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Video coverage plan '{coveragePlanId}' was not found. Video intent comes from B-100, so the "
                        + "plan must exist before the composer can open on it.");

                var coverage = plan.VideoCoveragePlans.FirstOrDefault(item => item.Id == coveragePlanId)
                    ?? throw new InvalidOperationException(
                        $"Plan '{plan.Id}' does not carry a video coverage plan '{coveragePlanId}'. Reload the plan or "
                        + "open the composer standalone.");

                var firstFrame = coverage.References.FirstOrDefault(reference =>
                    reference.Role == TypedMediaReferenceRole.VideoFirstFrame && reference.Required)
                    ?? coverage.References.FirstOrDefault(reference =>
                        reference.Role == TypedMediaReferenceRole.VideoFirstFrame);

                if (firstFrame is null || string.IsNullOrWhiteSpace(firstFrame.SourceRecordId))
                {
                    // An explicit diagnostic beats guessing: the coverage plan may legitimately carry no
                    // first-frame image, and the composer still opens standalone with the plan's intent seeded.
                    _logger.LogInformation(
                        "Video coverage plan {CoveragePlanId} carries no VideoFirstFrame source record; opening the "
                        + "composer with its intent only.",
                        coveragePlanId);

                    return new SceneVideoSeed(
                        SceneVideoOriginKind.VideoCoveragePlan,
                        coveragePlanId,
                        FileRelativePath: null,
                        FileName: null,
                        SuggestedTitle: $"Coverage {coverage.CoverageKey}",
                        CoverageCameraIntent: coverage.CameraIntent,
                        CoverageMotionIntent: coverage.MotionIntent,
                        CoverageDurationHint: coverage.DurationFitPolicy,
                        SessionId: null,
                        InteractionId: null);
                }

                var seedImage = await _sceneImages.GetImageAsync(firstFrame.SourceRecordId, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Video coverage plan '{coveragePlanId}' names scene image '{firstFrame.SourceRecordId}' as its "
                        + "first frame, but that image no longer exists. Regenerate the plan or open the composer "
                        + "standalone.");

                if (string.IsNullOrWhiteSpace(seedImage.FileRelativePath))
                {
                    throw new InvalidOperationException(
                        $"The first frame of video coverage plan '{coveragePlanId}' has no stored file.");
                }

                return new SceneVideoSeed(
                    SceneVideoOriginKind.VideoCoveragePlan,
                    coveragePlanId,
                    seedImage.FileRelativePath,
                    Path.GetFileName(seedImage.FileRelativePath),
                    $"Coverage {coverage.CoverageKey}",
                    CoverageCameraIntent: coverage.CameraIntent,
                    CoverageMotionIntent: coverage.MotionIntent,
                    CoverageDurationHint: coverage.DurationFitPolicy,
                    SessionId: seedImage.SessionId,
                    InteractionId: seedImage.InteractionId);
            }

            default:
                throw new InvalidOperationException(
                    $"Video origin kind '{originKind}' is not a supported origin for the Video Composer.");
        }
    }

    public async Task<byte[]> ReadReferenceBytesAsync(
        string fileRelativePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileRelativePath))
        {
            throw new InvalidOperationException("A reference image path is required to read its bytes.");
        }

        await using var stream = await _imageStorage.OpenReadAsync(fileRelativePath, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    public async Task<byte[]> ReadReferenceBytesAsync(
        SceneVideoReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (reference.StorageRoot == SceneVideoReferenceStorageRoot.SceneVideo)
        {
            if (string.IsNullOrWhiteSpace(reference.FileRelativePath))
            {
                throw new InvalidOperationException(
                    "A scene-video reference needs its relative path; nothing was read.");
            }

            await using var videoStream = await _videoStorage.OpenReadAsync(
                reference.FileRelativePath, cancellationToken);
            using var videoBuffer = new MemoryStream();
            await videoStream.CopyToAsync(videoBuffer, cancellationToken);
            return videoBuffer.ToArray();
        }

        return await ReadReferenceBytesAsync(reference.FileRelativePath, cancellationToken);
    }

    public async Task<SceneVideoContinuationSeed> PrepareContinuationAsync(
        string sourceVideoId,
        SceneVideoContinuationKind kind,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceVideoId))
        {
            throw new InvalidOperationException(
                "Continuing a clip needs the source composition id; an empty id has no clip to continue.");
        }

        var source = await _repository.GetAsync(sourceVideoId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Composition '{sourceVideoId}' was not found, so there is no clip to continue.");

        if (source.Status != SceneVideoStatus.Complete)
        {
            throw new InvalidOperationException(
                $"Composition '{sourceVideoId}' is {source.Status}, so it has no finished clip to continue. Only a "
                + "Complete clip can be continued.");
        }

        var clipRelativePath = source.NormalizedFileRelativePath ?? source.FileRelativePath;
        if (string.IsNullOrWhiteSpace(clipRelativePath))
        {
            throw new InvalidOperationException(
                $"Composition '{sourceVideoId}' is Complete but has no stored file, so its final frame cannot be "
                + "extracted.");
        }

        // The model supplies the ffmpeg the extraction runs through, exactly as the render's own finishing step does.
        var model = await _modelResolution.ResolveVideoModelAsync(source.RequestedModelId, cancellationToken);

        var chainDepth = await _chainPolicy.ResolveDepthAsync(source, cancellationToken);
        SceneVideoChainPolicy.EnsureWithinBudget(chainDepth, model.H3.MaxContinuationChainLength, source.Id);

        var absoluteClip = _videoStorage.ResolveAbsolutePath(clipRelativePath);
        var frameRelativePath = $"{source.Id}/frames/last.png";
        var absoluteFrame = _videoStorage.ResolveAbsolutePath(frameRelativePath);

        await _audioProcessor.ExtractFinalFrameAsync(
            absoluteClip, absoluteFrame, model.H3.FfmpegPath, cancellationToken);

        // A carried track is extracted beside the frame, so the handler can materialise the guide from the record
        // alone (the folder is the frame's own, and the name is fixed - one derivation, in one place).
        string? audioRelativePath = null;
        if (kind == SceneVideoContinuationKind.FrameAndAudio)
        {
            audioRelativePath = $"{source.Id}/frames/last-audio.wav";
            await _audioProcessor.ExtractAudioAsync(
                absoluteClip,
                _videoStorage.ResolveAbsolutePath(audioRelativePath),
                model.H3.FfmpegPath,
                cancellationToken);
        }

        var frameSha256 = await ComputeFileSha256Async(absoluteFrame, cancellationToken);
        var chainRoot = await _chainPolicy.ResolveRootIdAsync(source, cancellationToken);

        _logger.LogInformation(
            "Continuation prepared: Source={SourceId}, Kind={Kind}, ChainDepth={ChainDepth}, Frame={Frame}, Sha={Sha}",
            source.Id, kind, chainDepth, frameRelativePath, frameSha256);

        return new SceneVideoContinuationSeed(
            Source: source,
            Kind: kind,
            FrameRelativePath: frameRelativePath,
            FrameSha256: frameSha256,
            FrameFileName: $"frame-{frameSha256[..8]}.png",
            GuideFrameIndex: 0,
            ChainDepth: chainDepth,
            ChainRootVideoId: chainRoot,
            SourceDurationSeconds: source.MeasuredDurationSeconds,
            AudioRelativePath: audioRelativePath);
    }

    public async Task<IReadOnlyList<SceneVideoRecord>> ListContinuationsAsync(
        string sourceVideoId, CancellationToken cancellationToken = default) =>
        await _repository.ListContinuationsAsync(sourceVideoId, cancellationToken);

    private static async Task<string> ComputeFileSha256Async(
        string absolutePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public async Task<SceneVideoRecord> EnqueueAsync(        SceneVideoComposeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var record = request.Record;
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.PromptSnapshot))
        {
            throw new InvalidOperationException(
                "A video composition needs its compiled prompt before it can be queued; the composer compiles and "
                + "validates it first, and a failing rule blocks queueing rather than submitting silently.");
        }

        if (request.References.Count == 0)
        {
            throw new InvalidOperationException(
                "A video composition needs at least one reference image. The H3 node conditions on reference slots, "
                + "and a composition with none is not the render this path produces.");
        }

        if (record.Length <= 0 || record.Width <= 0 || record.Height <= 0 || record.Steps <= 0)
        {
            throw new InvalidOperationException(
                "The video composition is missing its frame count, canvas or steps. Fill the Sampling & Output tab "
                + "before queueing.");
        }

        var now = DateTime.UtcNow;
        record.Id = string.IsNullOrWhiteSpace(record.Id) ? Guid.NewGuid().ToString("N") : record.Id;
        record.JobId = Guid.NewGuid().ToString("N");
        record.Status = SceneVideoStatus.Pending;
        record.ReferencesJson = JsonSerializer.Serialize(request.References, JsonOptions);
        record.CreatedUtc = now;
        record.UpdatedUtc = now;

        await _repository.InsertAsync(record, cancellationToken);

        var job = new DurableBackgroundJob
        {
            Id = record.JobId,
            JobType = BackgroundJobTypes.SceneVideoRendering,
            Lane = DurableJobLane.VideoRender,
            PayloadJson = JsonSerializer.Serialize(new SceneVideoRenderingJobPayload(record.Id), JsonOptions),
            DedupeKey = $"{BackgroundJobTypes.SceneVideoRendering}:{record.Id}",
            Status = request.StartImmediately
                ? DurableBackgroundJobStatus.Queued
                : DurableBackgroundJobStatus.Staged,
            MaxAttempts = 1,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        var enqueued = await _durableQueue.TryEnqueueAsync(job, cancellationToken);
        if (!enqueued)
        {
            throw new InvalidOperationException(
                $"The video render job for composition '{record.Id}' could not be admitted to the durable queue "
                + "(a job with the same dedupe key already exists).");
        }

        _logger.LogInformation(
            "Scene video composition admitted: RecordId={RecordId}, Status={Status}, References={References}, "
            + "Frames={Frames}, Steps={Steps}, Seed={Seed}",
            record.Id, job.Status, request.References.Count, record.Length, record.Steps, record.Seed);

        return record;
    }

    public async Task<SceneVideoRecord> StartStagedAsync(
        string recordId, CancellationToken cancellationToken = default)
    {
        var record = await RequireRecordAsync(recordId, cancellationToken);
        if (record.Status != SceneVideoStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Composition '{recordId}' is '{record.Status}' and cannot be started; only a staged composition can.");
        }

        if (string.IsNullOrWhiteSpace(record.JobId))
        {
            throw new InvalidOperationException(
                $"Composition '{recordId}' has no durable job to start; re-stage it from the composer.");
        }

        var activated = await _durableQueue.TryActivateAsync(record.JobId, DateTime.UtcNow, cancellationToken);
        if (!activated)
        {
            throw new InvalidOperationException(
                $"The staged job for composition '{recordId}' could not be started. Refresh the queue and try again.");
        }

        return record;
    }

    public async Task<SceneVideoRecord> CancelAsync(string recordId, CancellationToken cancellationToken = default)
    {
        var record = await RequireRecordAsync(recordId, cancellationToken);
        var now = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(record.JobId))
        {
            await _durableQueue.TryCancelAsync(record.JobId, now, cancellationToken);
        }

        await _repository.TryCancelAsync(record.Id, now, cancellationToken);
        return await RequireRecordAsync(recordId, cancellationToken);
    }

    public Task<SceneVideoRecord?> GetAsync(string recordId, CancellationToken cancellationToken = default) =>
        _repository.GetAsync(recordId, cancellationToken);

    public Task<IReadOnlyList<SceneVideoRecord>> ListRecentAsync(
        int limit, CancellationToken cancellationToken = default) =>
        _repository.ListRecentAsync(limit, cancellationToken);

    public Task<IReadOnlyList<SceneVideoRecord>> ListBySessionAsync(
        string sessionId, CancellationToken cancellationToken = default) =>
        _repository.ListBySessionAsync(sessionId, cancellationToken);

    private async Task<SceneVideoRecord> RequireRecordAsync(
        string recordId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            throw new InvalidOperationException("A composition id is required.");
        }

        return await _repository.GetAsync(recordId, cancellationToken)
            ?? throw new InvalidOperationException($"Video composition '{recordId}' was not found.");
    }

    private static string RequireOriginId(string? originId, string label)
    {
        if (string.IsNullOrWhiteSpace(originId))
        {
            throw new InvalidOperationException(
                $"This Video Composer route needs a {label} id, but none was supplied.");
        }

        return originId.Trim();
    }
}
