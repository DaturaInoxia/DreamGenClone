using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Records what a continuation's delivered clip actually measures against its anchor (B-156 C-14).
/// </summary>
/// <remarks>
/// The published chain-drift figures - roughly +4% contrast bloom and a third of the treble per join - come from
/// small, partly contradictory measurements, so this records evidence on the operator's own content rather than
/// asserting a magnitude. Measurement is observational (C-18): a statistic ffmpeg cannot produce is stored as absent
/// with a note, and a measurement failure never fails a render that has already delivered a file.
/// </remarks>
public sealed class SceneVideoDriftRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IVideoAudioProcessor _audioProcessor;
    private readonly ISceneVideoStorageService _storage;
    private readonly ILogger<SceneVideoDriftRecorder> _logger;

    public SceneVideoDriftRecorder(
        IVideoAudioProcessor audioProcessor,
        ISceneVideoStorageService storage,
        ILogger<SceneVideoDriftRecorder> logger)
    {
        _audioProcessor = audioProcessor;
        _storage = storage;
        _logger = logger;
    }

    public async Task RecordAsync(
        SceneVideoRecord record, string ffmpegPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Only a continuation has a predecessor to have drifted from; an original render records nothing.
        if (string.IsNullOrWhiteSpace(record.SourceVideoId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(record.NormalizedFileRelativePath))
        {
            Apply(record, null, "drift was not measured: the clip has no normalized file to measure");
            return;
        }

        try
        {
            var metrics = await _audioProcessor.MeasureDriftAsync(
                _storage.ResolveAbsolutePath(record.NormalizedFileRelativePath),
                string.IsNullOrWhiteSpace(record.SourceFrameRelativePath)
                    ? null
                    : _storage.ResolveAbsolutePath(record.SourceFrameRelativePath),
                ffmpegPath,
                cancellationToken);

            Apply(record, JsonSerializer.Serialize(metrics, JsonOptions), null);

            _logger.LogInformation(
                "Drift recorded for continuation {RecordId}: {Metrics}", record.Id, record.DriftMetricsJson);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Apply(record, null, $"drift was not measured ({ex.Message})");
            _logger.LogWarning(ex, "Drift could not be measured for continuation {RecordId}.", record.Id);
        }
    }

    private static void Apply(SceneVideoRecord record, string? metricsJson, string? note)
    {
        record.DriftMetricsJson = metricsJson;

        if (note is null)
        {
            return;
        }

        record.VerificationNotes = string.IsNullOrWhiteSpace(record.VerificationNotes)
            ? $"{note}."
            : $"{record.VerificationNotes} {note}.";
    }
}
