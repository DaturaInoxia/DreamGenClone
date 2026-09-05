using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageIdentityReadinessTests
{
    [Fact]
    public async Task ResolveIdentityReadiness_UsesFrozenVisibleNameUnionAndPreservesOrder()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Identity.Add("char-a", "A", "pack-a", 3, "face-a", "refs/a.png", "SHA-A");
        fixture.Identity.Add("char-b", "B", "pack-b", 7, "face-b", "refs/b.png", "SHA-B");
        fixture.Enrichment.Value.FrozenStateContractJson = JsonSerializer.Serialize(new SceneMomentFrozenStateContract(
            "visual",
            [
                Character("char-a", "A", ["B"]),
                Character("char-b", "B", ["A"])
            ],
            "room", "night", "lamps", "quiet", "tense", [], "stable"));

        var readiness = await fixture.Service.ResolveIdentityReadinessAsync("group-1");

        Assert.Collection(
            readiness,
            first =>
            {
                Assert.Equal("char-a", first.CharacterId);
                Assert.Equal("pack-a", first.IdentityPackId);
                Assert.Equal(3, first.IdentityPackVersion);
                Assert.Equal("SHA-A", first.Sha256);
            },
            second =>
            {
                Assert.Equal("char-b", second.CharacterId);
                Assert.Equal("pack-b", second.IdentityPackId);
                Assert.Equal(7, second.IdentityPackVersion);
                Assert.Equal("SHA-B", second.Sha256);
            });
    }

    [Fact]
    public async Task ResolveIdentityReadiness_MissingPackNamesExactCharacter()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Identity.Add("char-a", "A", "pack-a", 3, "face-a", "refs/a.png", "SHA-A");
        fixture.Enrichment.Value.FrozenStateContractJson = JsonSerializer.Serialize(new SceneMomentFrozenStateContract(
            "visual",
            [
                Character("char-a", "A", ["A", "B"]),
                Character("char-b", "B", ["A", "B"])
            ],
            "room", "night", "lamps", "quiet", "tense", [], "stable"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.ResolveIdentityReadinessAsync("group-1"));

        Assert.Contains("Character 'B'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveIdentityReadiness_IncludesCastCharacterWhoseSightlineNamesAnotherCharacter()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Identity.Add("char-a", "A", "pack-a", 3, "face-a", "refs/a.png", "SHA-A");
        fixture.Enrichment.Value.FrozenStateContractJson = JsonSerializer.Serialize(new SceneMomentFrozenStateContract(
            "visual",
            [Character("char-a", "A", ["B"])],
            "room", "night", "lamps", "quiet", "tense", [], "stable"));

        var readiness = await fixture.Service.ResolveIdentityReadinessAsync("group-1");

        var result = Assert.Single(readiness);
        Assert.Equal("char-a", result.CharacterId);
        Assert.Equal("pack-a", result.IdentityPackId);
    }

    private static SceneMomentFrozenCharacter Character(string id, string name, IReadOnlyList<string> visible)
        => new(id, id, name, "active", "room", "center", "standing", "direct", visible, "plain");

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _dbPath;
        public SceneImageProductionService Service { get; }
        public StubIdentityRepository Identity { get; } = new();
        public StubEnrichmentRepository Enrichment { get; } = new();

        private Fixture(string dbPath, StubGroupRepository groups)
        {
            _dbPath = dbPath;
            var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
            Service = new SceneImageProductionService(
                groups,
                new SceneImageRepository(options),
                new SceneAssetRepository(options),
                new NullSceneImageStorage(),
                new AcceptingGuard(),
                TimeProvider.System,
                null!, null!, Enrichment, null!, null!, Identity,
                NullLogger<SceneImageProductionService>.Instance);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"scene-image-readiness-{Guid.NewGuid():N}.db");
            var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={path};Pooling=False" });
            var groups = new StubGroupRepository();
            var fixture = new Fixture(path, groups);
            await groups.CreateAsync(new SceneImageProductionGroup
            {
                Id = "group-1", SessionId = "session-1", InteractionId = "interaction-1",
                CatalogueId = "catalogue", BeatId = "beat", BeatProductionPlanId = "plan",
                BeatProductionPlanVersion = 1, MomentSetId = "set", MomentSetVersion = 1,
                MomentId = "moment", MomentEnrichmentId = "enrichment-1", MomentEnrichmentRevision = 1,
                Pov = "Director", Status = SceneImageProductionGroupStatus.InProgress,
                CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow
            });
            fixture.Enrichment.Value = new SceneMomentEnrichment
            {
                Id = "enrichment-1", Status = SceneBeatCatalogueStatus.Complete,
                FrozenStateContractJson = "{}"
            };
            return fixture;
        }

        public ValueTask DisposeAsync()
        {
            try { File.Delete(_dbPath); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AcceptingGuard : ISceneImageProductionSessionGuard
    {
        public Task RequireCurrentAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubGroupRepository : ISceneImageProductionGroupRepository
    {
        private readonly SceneImageProductionGroup _group = new()
        {
            Id = "group-1", SessionId = "session-1", InteractionId = "interaction-1",
            MomentEnrichmentId = "enrichment-1", MomentEnrichmentRevision = 1
        };
        public Task<SceneImageProductionGroup?> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<SceneImageProductionGroup?>(id == "group-1" ? _group : null);
        public Task<SceneImageAttemptRetentionPolicy?> GetRetentionPolicyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneImageAttemptRetentionPolicy> SaveRetentionPolicyAsync(SceneImageAttemptRetentionPolicy policy, long? expectedVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateAsync(SceneImageProductionGroup group, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SceneImageProductionGroup?> GetCurrentAsync(string momentEnrichmentId, string pov, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneImageProductionGroup> SetIdentityPolicyAsync(string groupId, SceneImageIdentityPolicy policy, string? reason, DateTime updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneImageProductionGroup>> ListByInteractionAsync(string sessionId, string interactionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApprovedSceneFrameDecision?> GetApprovalDecisionAsync(string decisionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ApprovedSceneFrameDecision>> ListApprovalDecisionsAsync(string groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApprovedSceneFrameDecision> ApproveAsync(string groupId, string imageId, string sha256, string decidedBy, string? note, DateTime decisionUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApprovedSceneFrameDecision> RevokeCurrentApprovalAsync(string groupId, string decidedBy, string? note, DateTime decisionUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NullSceneImageStorage : ISceneImageStorageService
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> SaveAsync(string sessionId, string fileName, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubEnrichmentRepository : ISceneMomentEnrichmentRepository
    {
        public SceneMomentEnrichment Value { get; set; } = new();
        public Task<SceneMomentEnrichment?> GetAsync(string enrichmentId, CancellationToken cancellationToken = default) => Task.FromResult<SceneMomentEnrichment?>(Value);
        public Task<SceneMomentEnrichment?> GetCurrentAsync(string momentSetId, string momentId, CancellationToken cancellationToken = default) => Task.FromResult<SceneMomentEnrichment?>(Value);
        public Task CreateRevisionAsync(SceneMomentEnrichment enrichment, SceneBeatAnalysisAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryStartAttemptAsync(string enrichmentId, string attemptId, string modelIdentifier, string providerName, DateTime startedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAttemptAsync(string enrichmentId, SceneBeatAnalysisAttempt attempt, SceneMomentEnrichmentData data, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryFailAttemptAsync(string enrichmentId, SceneBeatAnalysisAttempt attempt, string errorCode, string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCancelCurrentAsync(string enrichmentId, string attemptId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubIdentityRepository : ICharacterImageIdentityRepository
    {
        private readonly Dictionary<string, (CharacterImageIdentityPack Pack, SceneImageReferenceAsset Asset)> _items = [];
        public void Add(string characterId, string name, string packId, int version, string faceId, string path, string sha)
        {
            _items[characterId] = (new CharacterImageIdentityPack { Id = packId, CharacterProfileId = characterId, Version = version, Status = CharacterImageIdentityPackStatus.Approved, CanonicalFaceAssetId = faceId }, new SceneImageReferenceAsset { Id = faceId, IdentityPackId = packId, AssetKind = SceneImageReferenceAssetKind.Face, IsApproved = true, FileRelativePath = path, Sha256 = sha });
        }
        public Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(string characterProfileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CharacterImageIdentityPack>>(_items.TryGetValue(characterProfileId, out var value) ? [value.Pack] : []);
        public Task<SceneImageReferenceAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default) => Task.FromResult(_items.Values.Select(value => value.Asset).FirstOrDefault(asset => asset.Id == assetId));
        public Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken = default) => Task.FromResult<CharacterImageIdentityPack?>(_items.Values.Select(value => value.Pack).FirstOrDefault(pack => pack.Id == packId));
        public Task<CharacterImageIdentityPack?> GetLatestApprovedPackAsync(string characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterImageIdentityPack> UpsertDraftAsync(CharacterImageIdentityPack pack, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterImageIdentityPack> ApproveAsync(string packId, string descriptorSnapshotJson, string canonicalFaceAssetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterImageIdentityPack> SupersedeAsync(string packId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePackAsync(string packId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAssetAsync(SceneImageReferenceAsset asset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(string packId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAssetProvenanceAsync(string assetId, string sourceLabel, SceneImageReferenceConsentState consentState, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetAssetApprovalAsync(string assetId, bool isApproved, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAssetQualityAsync(string assetId, SceneImageReferenceQuality quality, string qualityNotes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountAssetsByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
