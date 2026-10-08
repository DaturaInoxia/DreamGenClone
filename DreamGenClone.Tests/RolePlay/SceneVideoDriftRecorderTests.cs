using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Pins B-156's drift recording (C-14/C-18). The measurement is evidence, not a gate: it is written for continuations
/// only, and a statistic that cannot be produced is recorded as absent with a note rather than failing a render that
/// has already delivered a file.
/// </summary>
public sealed class SceneVideoDriftRecorderTests
{
    private static readonly SceneVideoDriftMetrics SampleMetrics = new(
        LuminanceMean: 96.5,
        LuminanceLowPercentile: 12.25,
        LuminanceHighPercentile: 240.5,
        SaturationMean: 44.25,
        NearBlackPercent: null,
        EdgeEnergyMean: 8.5,
        EdgeEnergyRatioToSource: 0.93,
        AudioTrebleToBassRatio: 2.5,
        Notes: "Measured with ffmpeg signalstats, edgedetect and astats.");

    [Fact]
    public async Task Record_LeavesAnOriginalRenderAlone()
    {
        var processor = new StubAudioProcessor { Result = SampleMetrics };
        var record = NewRecord(sourceVideoId: null, normalizedFileRelativePath: "clip/norm.mp4");

        await NewRecorder(processor).RecordAsync(record, @"C:\ffmpeg.exe");

        Assert.Null(record.DriftMetricsJson);
        Assert.Equal(0, processor.MeasureCalls);
    }

    [Fact]
    public async Task Record_StoresTheMeasuredMetricsForAContinuation()
    {
        var processor = new StubAudioProcessor { Result = SampleMetrics };
        var record = NewRecord("source-clip", "clip/norm.mp4");
        record.SourceFrameRelativePath = "source-clip/frames/last.png";

        await NewRecorder(processor).RecordAsync(record, @"C:\ffmpeg.exe");

        Assert.NotNull(record.DriftMetricsJson);
        var read = JsonSerializer.Deserialize<SceneVideoDriftMetrics>(
            record.DriftMetricsJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(SampleMetrics, read);
        Assert.Null(record.VerificationNotes);

        // The measurement reads the delivered (normalized) clip and compares it against the stored anchor frame.
        Assert.EndsWith("clip/norm.mp4", Normalize(processor.MeasuredClipPath));
        Assert.EndsWith("source-clip/frames/last.png", Normalize(processor.MeasuredSourceFramePath));
    }

    [Fact]
    public async Task Record_TurnsAMeasurementFailureIntoANoteRatherThanAFailedRender()
    {
        var processor = new StubAudioProcessor
        {
            Failure = new InvalidOperationException("ffmpeg exited 1 (no such filter)")
        };
        var record = NewRecord("source-clip", "clip/norm.mp4");
        record.VerificationNotes = "h264 + aac 5.2s";

        await NewRecorder(processor).RecordAsync(record, @"C:\ffmpeg.exe");

        Assert.Null(record.DriftMetricsJson);
        Assert.NotNull(record.VerificationNotes);
        Assert.StartsWith("h264 + aac 5.2s", record.VerificationNotes);
        Assert.Contains("ffmpeg exited 1 (no such filter)", record.VerificationNotes);
    }

    [Fact]
    public async Task Record_NotesAContinuationWithNoNormalizedFileInsteadOfMeasuringTheRawOne()
    {
        var processor = new StubAudioProcessor { Result = SampleMetrics };
        var record = NewRecord("source-clip", normalizedFileRelativePath: null);
        record.FileRelativePath = "clip/raw.mp4";

        await NewRecorder(processor).RecordAsync(record, @"C:\ffmpeg.exe");

        Assert.Null(record.DriftMetricsJson);
        Assert.Equal(0, processor.MeasureCalls);
        Assert.Contains("normalized", record.VerificationNotes);
    }

    private static string Normalize(string? path) => (path ?? string.Empty).Replace('\\', '/');

    private static SceneVideoDriftRecorder NewRecorder(IVideoAudioProcessor processor) =>
        new(processor, new StubStorage(), NullLogger<SceneVideoDriftRecorder>.Instance);

    private static SceneVideoRecord NewRecord(string? sourceVideoId, string? normalizedFileRelativePath) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Title = string.Empty,
        Status = SceneVideoStatus.Complete,
        OriginKind = SceneVideoOriginKind.Standalone,
        PromptSnapshot = "the compiled six-section document",
        SettingsJson = "{}",
        NormalizedFileRelativePath = normalizedFileRelativePath,
        SourceVideoId = sourceVideoId,
        Length = 124,
        Width = 1344,
        Height = 768,
        Steps = 40,
        Fps = 24,
        RefImageSize = "match",
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow
    };

    private sealed class StubStorage : ISceneVideoStorageService
    {
        public string ResolveAbsolutePath(string relativePath) => $@"D:\scene-videos\{relativePath}";

        public Task<string> SaveAsync(
            string recordId, string fileName, Stream content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubAudioProcessor : IVideoAudioProcessor
    {
        public SceneVideoDriftMetrics? Result { get; init; }

        public Exception? Failure { get; init; }

        public int MeasureCalls { get; private set; }

        public string? MeasuredClipPath { get; private set; }

        public string? MeasuredSourceFramePath { get; private set; }

        public Task<SceneVideoDriftMetrics> MeasureDriftAsync(
            string absolutePath,
            string? sourceFrameAbsolutePath,
            string ffmpegPath,
            CancellationToken cancellationToken = default)
        {
            MeasureCalls++;
            MeasuredClipPath = absolutePath;
            MeasuredSourceFramePath = sourceFrameAbsolutePath;

            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(Result!);
        }

        public Task<SceneVideoAudioEvidence> ProbeAsync(
            string absolutePath, string ffmpegPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(string NormalizedAbsolutePath, SceneVideoAudioEvidence Evidence)> NormalizeAndVerifyAsync(
            string absolutePath,
            string ffmpegPath,
            double targetLufs,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ExtractFinalFrameAsync(
            string absolutePath,
            string destinationAbsolutePath,
            string ffmpegPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ExtractAudioAsync(
            string absolutePath,
            string destinationAbsolutePath,
            string ffmpegPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
