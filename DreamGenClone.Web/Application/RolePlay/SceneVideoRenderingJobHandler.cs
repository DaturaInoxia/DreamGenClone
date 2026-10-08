using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Renders one composed clip: resolves the video model, uploads the ordered references, renders through the H3
/// client, stores the mp4, then normalizes and MEASURES the delivered audio (B-152, S3/S6).
/// </summary>
/// <remarks>
/// The record carries the whole render (prompt snapshot, ordered reference manifest, canvas, sampling, LoRA stack),
/// so this handler re-reads nothing from the UI and a re-delivered job cannot render a different clip. Completion is
/// claimed-gated (<see cref="ISceneVideoRepository.TryClaimAsync"/>): only the run that claimed the row writes its
/// result, and the stored row carries the measured stream/loudness evidence so a silent clip shows up as a
/// verification failure rather than a surprise at review time.
/// </remarks>
public sealed class SceneVideoRenderingJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneVideoRepository _repository;
    private readonly ISceneVideoStorageService _storage;
    private readonly IVideoGenerationClient _videoClient;
    private readonly IVideoAudioProcessor _audioProcessor;
    private readonly IModelResolutionService _modelResolution;
    private readonly SceneVideoDriftRecorder _driftRecorder;
    private readonly ISceneVideoService _sceneVideoService;
    private readonly ILogger<SceneVideoRenderingJobHandler> _logger;

    public SceneVideoRenderingJobHandler(
        ISceneVideoRepository repository,
        ISceneVideoStorageService storage,
        IVideoGenerationClient videoClient,
        IVideoAudioProcessor audioProcessor,
        IModelResolutionService modelResolution,
        SceneVideoDriftRecorder driftRecorder,
        ISceneVideoService sceneVideoService,
        ILogger<SceneVideoRenderingJobHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _videoClient = videoClient;
        _audioProcessor = audioProcessor;
        _modelResolution = modelResolution;
        _driftRecorder = driftRecorder;
        _sceneVideoService = sceneVideoService;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SceneVideoRendering;

    public async Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var payload = JsonSerializer.Deserialize<SceneVideoRenderingJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new DurableJobFailureException(
                "scene_video_payload_invalid",
                $"Video job '{job.Id}' carries no readable payload.",
                isTransient: false);

        var record = await _repository.GetAsync(payload.RecordId, cancellationToken);
        if (record is null)
        {
            throw new DurableJobFailureException(
                "scene_video_record_missing",
                $"Video composition '{payload.RecordId}' no longer exists.",
                isTransient: false);
        }

        if (record.Status != SceneVideoStatus.Pending)
        {
            // Idempotency: a re-delivered job must not re-render (or overwrite) a finished composition.
            _logger.LogInformation(
                "Video job {JobId} skipped: composition {RecordId} is already '{Status}'.",
                job.Id, record.Id, record.Status);
            return;
        }

        if (!await _repository.TryClaimAsync(record.Id, DateTime.UtcNow, cancellationToken))
        {
            _logger.LogInformation(
                "Video job {JobId} skipped: composition {RecordId} was claimed by another run.", job.Id, record.Id);
            return;
        }

        try
        {
            var model = await _modelResolution.ResolveVideoModelAsync(record.RequestedModelId, cancellationToken);
            var references = JsonSerializer.Deserialize<List<SceneVideoReference>>(
                record.ReferencesJson ?? "[]", JsonOptions) ?? [];

            var images = new List<SceneVideoReferenceImage>(references.Count);
            foreach (var reference in references)
            {
                // The reference names its own root: a continuation's anchor frame lives under the SCENE-VIDEO root,
                // while every other reference is a scene image. Reading by path alone sent the anchor to the
                // scene-image root and failed the render looking for a file that was never there.
                var bytes = await _sceneVideoService.ReadReferenceBytesAsync(reference, cancellationToken);
                images.Add(new SceneVideoReferenceImage(bytes, reference.FileName));
            }

            var sceneLoras = DeserializeLoras(record.LoraStackJson);

            // B-156: a continuation materialises its guide from the record alone - the frame extracted when the
            // operator pressed Continue, plus (for a carried track) the audio beside it. A continuation that cannot
            // produce its guide FAILS here rather than rendering a clip the operator would believe is continuous.
            SceneVideoGuideImage? guideImage = null;
            SceneVideoGuideImage? guideAudio = null;
            if (!string.IsNullOrWhiteSpace(record.SourceVideoId))
            {
                if (record.ContinuationKind is null)
                {
                    throw new InvalidOperationException(
                        $"Composition '{record.Id}' names source '{record.SourceVideoId}' but has no continuation "
                        + "kind, so the guide it should render is undefined.");
                }

                if (string.IsNullOrWhiteSpace(record.SourceFrameRelativePath))
                {
                    throw new InvalidOperationException(
                        $"Composition '{record.Id}' is a continuation of '{record.SourceVideoId}' but has no stored "
                        + "source frame, so its anchor does not exist. Prepare the continuation again.");
                }

                guideImage = new SceneVideoGuideImage(
                    await ReadSceneVideoFileAsync(record.SourceFrameRelativePath, cancellationToken),
                    $"frame-{Path.GetFileName(record.SourceFrameRelativePath)}");

                if (record.ContinuationKind == SceneVideoContinuationKind.FrameAndAudio)
                {
                    var audioRelativePath =
                        $"{Path.GetDirectoryName(record.SourceFrameRelativePath)?.Replace('\\', '/')}/last-audio.wav";
                    guideAudio = new SceneVideoGuideImage(
                        await ReadSceneVideoFileAsync(audioRelativePath, cancellationToken),
                        "last-audio.wav");
                }
            }

            var request = new SceneVideoGenerationRequest(
                Prompt: record.PromptSnapshot,
                References: images,
                Width: record.Width,
                Height: record.Height,
                Length: record.Length,
                Steps: record.Steps,
                Seed: record.Seed,
                RefImageSize: record.RefImageSize,
                OutputPrefix: $"scene-videos/{record.Id}",
                Loras: sceneLoras,
                GuideImage: guideImage,
                GuideAudio: guideAudio,
                GuideFrameIndex: guideImage is null ? null : record.GuideFrameIndex);

            var result = await _videoClient.GenerateAsync(model, request, cancellationToken);

            await using (var content = new MemoryStream(result.VideoBytes, writable: false))
            {
                record.FileRelativePath = await _storage.SaveAsync(
                    record.Id, result.OutputFileName, content, cancellationToken);
            }

            await FinishWithVerifiedAudioAsync(record, model, result.OutputFileName, cancellationToken);

            // B-156 C-14/C-18: a continuation records what its block measures against the anchor. This runs after the
            // verification notes exist so an unmeasurable statistic is appended to them, and it never fails a render
            // that has already delivered a file.
            await _driftRecorder.RecordAsync(record, model.H3.FfmpegPath, cancellationToken);

            record.ModelIdentifier = model.ModelIdentifier;
            record.ProviderName = model.ProviderName;
            record.LoudnessTargetLufs = model.H3.LoudnessTargetLufs;
            record.CompletedUtc = DateTime.UtcNow;
            record.UpdatedUtc = record.CompletedUtc.Value;

            if (!await _repository.TryCompleteAsync(record, cancellationToken))
            {
                _logger.LogWarning(
                    "Video composition {RecordId} finished but its row was no longer claimable; the file was stored "
                    + "and the record was left as it was.",
                    record.Id);
            }

            _logger.LogInformation(
                "Scene video render complete: RecordId={RecordId}, File={File}, Normalized={Normalized}, "
                + "VideoStream={VideoStream}, AudioStream={AudioStream}, Loudness={Loudness}",
                record.Id, record.FileRelativePath, record.NormalizedFileRelativePath,
                record.VideoStreamPresent, record.AudioStreamPresent, record.MeasuredLoudnessLufs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A cancelled job leaves the row as it is: the cancellation path owns the terminal transition.
            throw;
        }
        catch (Exception ex)
        {
            record.ErrorMessage = ex.Message;
            record.CompletedUtc = DateTime.UtcNow;
            record.UpdatedUtc = record.CompletedUtc.Value;
            await _repository.TryFailAsync(record, CancellationToken.None);

            _logger.LogError(ex, "Scene video render failed: RecordId={RecordId}", record.Id);
            throw;
        }
    }

    /// <summary>
    /// Normalizes the delivered audio to the model's configured loudness target and records what was MEASURED. This
    /// is mandatory (H3's native audio is ~12 dB too quiet), so a missing ffmpeg fails the render instead of storing
    /// an inaudible clip that looks successful.
    /// </summary>
    private async Task FinishWithVerifiedAudioAsync(
        SceneVideoRecord record,
        ResolvedVideoModel model,
        string outputFileName,
        CancellationToken cancellationToken)
    {
        var rawAbsolutePath = _storage.ResolveAbsolutePath(record.FileRelativePath!);
        var (normalizedAbsolutePath, evidence) = await _audioProcessor.NormalizeAndVerifyAsync(
            rawAbsolutePath, model.H3.FfmpegPath, model.H3.LoudnessTargetLufs, cancellationToken);

        var relativeDirectory = Path.GetDirectoryName(record.FileRelativePath);
        var normalizedName = Path.GetFileName(normalizedAbsolutePath);
        record.NormalizedFileRelativePath = string.IsNullOrEmpty(relativeDirectory)
            ? normalizedName
            : $"{relativeDirectory}/{normalizedName}";

        record.VideoStreamPresent = evidence.VideoStreamPresent;
        record.AudioStreamPresent = evidence.AudioStreamPresent;
        record.MeasuredLoudnessLufs = evidence.IntegratedLoudnessLufs;
        record.MeasuredDurationSeconds = evidence.DurationSeconds;
        record.VerificationNotes = evidence.Notes;

        _logger.LogInformation(
            "Scene video verified: RecordId={RecordId}, Raw={Raw}, Normalized={Normalized}, Duration={Duration}s",
            record.Id, outputFileName, record.NormalizedFileRelativePath, evidence.DurationSeconds);
    }

    /// <summary>Reads a file under the scene-video root (the continuation guide's frame and audio live there).</summary>
    private async Task<byte[]> ReadSceneVideoFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        await using var stream = await _storage.OpenReadAsync(relativePath, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static IReadOnlyList<ResolvedSceneLora> DeserializeLoras(string? json)    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var entries = JsonSerializer.Deserialize<List<SceneVideoLoraEntry>>(json, JsonOptions) ?? [];
        return entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.FileName))
            .Select(entry => new ResolvedSceneLora(
                entry.FileName,
                entry.Strength,
                Purpose: entry.Purpose,
                TriggerToken: entry.TriggerToken))
            .ToList();
    }

    /// <summary>The stored LoRA stack's shape - the same fields the composer's picker produced.</summary>
    public sealed record SceneVideoLoraEntry(
        string FileName,
        double Strength,
        string? Purpose = null,
        string? TriggerToken = null);
}
