using System.Numerics;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The test doubles the pose-suite tests share: the pose library, the character roster, and an in-memory suite store.
///
/// <para>
/// Each one implements exactly what a pose suite touches and throws on everything else, so a test that accidentally
/// depends on another member fails loudly instead of silently using a default. The suite store is a RECORDING one
/// because what a derived suite does to existing cells (updates them, and removes the ones whose pose is gone) is the
/// behaviour under test, not an implementation detail.
/// </para>
/// </summary>
internal sealed class StubPoseLibraryService : IPoseLibraryService
{
    public List<PoseLibrary> Libraries { get; } = [];

    public List<PosePreset> Presets { get; } = [];

    public Task<IReadOnlyList<PoseLibrary>> ListLibrariesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PoseLibrary>>(Libraries);

    public Task<IReadOnlyList<PosePreset>> SearchAsync(
        PoseLibraryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult<IReadOnlyList<PosePreset>>(Presets
            .Where(preset => string.IsNullOrWhiteSpace(query.LibraryId)
                || string.Equals(preset.LibraryId, query.LibraryId, StringComparison.Ordinal))
            .Where(preset => string.IsNullOrWhiteSpace(query.Category)
                || string.Equals(preset.Category, query.Category, StringComparison.OrdinalIgnoreCase))
            .Where(preset => string.IsNullOrWhiteSpace(query.Keyword)
                || preset.Name.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase))
            .ToList());
    }

    public Task<PosePreset?> GetPresetAsync(string presetId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Presets.FirstOrDefault(preset =>
            string.Equals(preset.Id, presetId, StringComparison.Ordinal)));

    public Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(
            Presets.Select(preset => preset.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

    public Task<byte[]> ReadSkeletonAsync(string presetId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new byte[] { 1, 2, 3 });

    public PoseLibrary CreateLibrary(string name, string description) => new()
    {
        Id = $"library-{Libraries.Count + 1}",
        Name = name,
        Description = description
    };

    public Task<PoseLibrary> CreateLibraryAsync(
        string name, string description, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PoseLibrary> EnsureAuthoredLibraryAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public PosePerson ProjectAuthoredPose(PoseView view, PoseHeadRotation? head = null, Quaternion[]? rotations = null) =>
        throw new NotSupportedException();

    public PosePerson ProjectLoadedPose(
        PosePerson stored, PoseView fittedView, PoseView view, Quaternion[] rotations, PoseHeadRotation? head = null) =>
        throw new NotSupportedException();

    public byte[] RenderAuthoredPreview(PoseView view, PoseHeadRotation? head, int canvas, Quaternion[]? rotations = null) =>
        throw new NotSupportedException();

    public byte[] RenderAuthoredHeadPreview(PoseView view, PoseHeadRotation head, int canvas) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<string>> ExportProjectedPosesAsync(
        string directory, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PosePreset> SaveAuthoredPoseAsync(
        AuthoredPoseRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PosePreset> OverwritePoseAsync(
        string id, AuthoredPoseRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public string? SkeletonUrl(PosePreset preset) => preset.SkeletonPngPath;
}

/// <summary>The characters that have an approved pack. Only the roster read a run needs.</summary>
internal sealed class RecordingIdentityRoster : ICharacterImageIdentityService
{
    public List<IdentityPackOwner> Owners { get; } = [];

    public Task<IReadOnlyList<IdentityPackOwner>> ListPackOwnersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IdentityPackOwner>>(Owners);

    public Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(
        string characterProfileId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<CharacterImageIdentityPack?> GetPackAsync(
        string packId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(
        string packId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<byte[]> ReadAssetBytesAsync(
        SceneImageReferenceAsset asset, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<CharacterImageIdentityPack> CreateDraftPackAsync(
        string characterProfileId, CharacterImageIdentityPackScope scope, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<CharacterImageIdentityPack> SetDraftPackScopeAsync(
        string packId, CharacterImageIdentityPackScope scope, string? canonicalFullBodyAssetId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<CharacterImageIdentityPack> ApprovePackAsync(
        string packId, string descriptorSnapshotJson, string canonicalFaceAssetId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<CharacterImageIdentityPack> SupersedePackAsync(
        string packId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DeletePackAsync(string packId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<SceneImageReferenceAsset> UploadAssetAsync(
        string packId, SceneImageReferenceAssetKind kind, string fileName, Stream content,
        SceneImageReferenceFaceView? faceView = null, SceneImageReferenceBodyView? bodyView = null,
        SceneImageReferenceBodyState? bodyState = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<SceneImageReferenceSlotWrite> ReplaceSlotAssetAsync(
        string packId, SceneImageReferenceAssetKind kind, string fileName, Stream content,
        SceneImageReferenceFaceView? faceView = null, SceneImageReferenceBodyView? bodyView = null,
        SceneImageReferenceBodyState? bodyState = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task SetAssetProvenanceAsync(
        string assetId, string sourceLabel, SceneImageReferenceConsentState consentState,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task SetAssetApprovalAsync(string assetId, bool isApproved, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task SetAssetQualityAsync(
        string assetId, SceneImageReferenceQuality quality, string qualityNotes,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<SceneImageReferenceAsset> AnalyzeAssetQualityAsync(
        string assetId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>An in-memory suite store that records what was written and what was removed.</summary>
internal sealed class RecordingSuiteRepository : IImageSuiteRepository
{
    private readonly Dictionary<string, ImageSuite> _suites = new(StringComparer.Ordinal);
    private readonly List<ImageSuiteCell> _cells = [];

    public List<string> DeletedCellIds { get; } = [];

    public IReadOnlyList<ImageSuiteCell> Cells => _cells;

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<ImageSuite>> ListSuitesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ImageSuite>>(_suites.Values.ToList());

    public Task<ImageSuite?> GetSuiteAsync(string suiteId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_suites.TryGetValue(suiteId, out var suite) ? suite : null);

    public Task UpsertSuiteAsync(ImageSuite suite, CancellationToken cancellationToken = default)
    {
        _suites[suite.Id] = suite;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ImageSuiteCell>> ListCellsAsync(
        string suiteId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ImageSuiteCell>>(
            _cells.Where(cell => cell.SuiteId == suiteId).OrderBy(cell => cell.Ordinal).ToList());

    public Task UpsertCellAsync(ImageSuiteCell cell, CancellationToken cancellationToken = default)
    {
        _cells.RemoveAll(existing => existing.Id == cell.Id);
        _cells.Add(cell);
        return Task.CompletedTask;
    }

    public Task DeleteCellAsync(string cellId, CancellationToken cancellationToken = default)
    {
        DeletedCellIds.Add(cellId);
        _cells.RemoveAll(cell => cell.Id == cellId);
        return Task.CompletedTask;
    }
}
