using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The head measurement seam the crop panel drives: it must measure the subject's real stored file, and it
/// must report "no head found" as null instead of substituting one.
/// </summary>
public sealed class MediaEditHeadMeasurementServiceTests
{
    [Fact]
    public async Task MeasureAsync_MeasuresTheSubjectsStoredFile()
    {
        var root = CreateRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "assets", "face.png"), "stub");
        var measurements = new RecordingMeasurementService(new CharacterIdentityHeadMeasurement(277, 820));
        var service = Create(measurements, root);

        var head = await service.MeasureAsync(ImageEditSubject.ForAssetImage("asset-1", "image-1"));

        Assert.NotNull(head);
        Assert.Equal(543, head!.HeadHeightPx);
        Assert.Equal(Path.Combine(root, "assets", "face.png"), measurements.LastImagePath);
    }

    [Fact]
    public async Task MeasureAsync_NoFaceMesh_ReturnsNull()
    {
        var root = CreateRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "assets", "face.png"), "stub");
        var service = Create(new RecordingMeasurementService(null), root);

        var head = await service.MeasureAsync(ImageEditSubject.ForAssetImage("asset-1", "image-1"));

        Assert.Null(head);
    }

    [Fact]
    public async Task MeasureAsync_UnavailableSubjectImage_FailsFast()
    {
        var root = CreateRoot();
        var service = Create(new RecordingMeasurementService(null), root, sourceAvailable: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MeasureAsync(ImageEditSubject.ForAssetImage("asset-1", "image-1")));

        Assert.Contains("image-1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MeasureAsync_MissingFileOnDisk_FailsFast()
    {
        var root = CreateRoot();
        var service = Create(new RecordingMeasurementService(null), root);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MeasureAsync(ImageEditSubject.ForAssetImage("asset-1", "image-1")));

        Assert.Contains("was not found at", error.Message, StringComparison.Ordinal);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"head-measure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        return root;
    }

    private static MediaEditHeadMeasurementService Create(
        ICharacterIdentityMeasurementService measurements, string root, bool sourceAvailable = true)
        => new(
            new ImageEditWorkspaceServiceResolver(
                [new StubWorkspaceService(sourceAvailable ? "assets/face.png" : null)], []),
            measurements,
            Options.Create(new PersistenceOptions { SceneImageRoot = root }));

    private sealed class RecordingMeasurementService : ICharacterIdentityMeasurementService
    {
        private readonly CharacterIdentityHeadMeasurement? _head;

        public RecordingMeasurementService(CharacterIdentityHeadMeasurement? head) => _head = head;

        public string? LastImagePath { get; private set; }

        public Task<CharacterIdentityMeasurementResult> MeasureFileAsync(
            string imagePath, CancellationToken cancellationToken = default)
        {
            LastImagePath = imagePath;
            return Task.FromResult(new CharacterIdentityMeasurementResult(
                new CharacterIdentityEyeMeasurement { Head = _head }, string.Empty));
        }
    }

    /// <summary>Only the source lookup matters here; every other member is out of scope for this seam.</summary>
    private sealed class StubWorkspaceService : IImageEditWorkspaceService
    {
        private readonly string? _fileRelativePath;

        public StubWorkspaceService(string? fileRelativePath) => _fileRelativePath = fileRelativePath;

        public ImageEditSubjectKind Kind => ImageEditSubjectKind.AssetImage;

        public bool SupportsIdentity => false;

        public Task<ImageEditSource?> GetSourceAsync(ImageEditSubject subject, CancellationToken cancellationToken = default)
            => Task.FromResult(_fileRelativePath is null
                ? null
                : new ImageEditSource(subject.ImageId, _fileRelativePath, "1024x1024", "Complete", true));

        public Task<ImageEditSessionView> OpenSessionAsync(ImageEditSubject subject, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImageEditSessionView> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReanalyzeAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ImageEditDescriptionOutcome?> GetDescriptionOutcomeAsync(
            string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImageEditAttemptView?> GetLatestAttemptAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImageEditAttemptView> PrepareAsync(string sessionId, string rawIntent, IReadOnlyList<string> clarificationHistory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ImageEditRevisionView>> ListRevisionsAsync(string compilationAttemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImageEditRevisionView> AppendRevisionAsync(string sessionId, string compilationAttemptId, string prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImageEditResultView> RunAsync(ImageEditRunRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImageEditResultView> RunCropAsync(ImageEditSubject subject, MediaEditCropOperation crop, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImageEditResultView> RunEnhanceAsync(ImageEditSubject subject, MediaEditEnhanceOperation enhance, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImageEditResultView?> ResolveResultAsync(ImageEditSubject subject, string? sessionId, string? trackedResultId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ImageEditLineageItem>> ListLineageAsync(ImageEditSubject subject, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
