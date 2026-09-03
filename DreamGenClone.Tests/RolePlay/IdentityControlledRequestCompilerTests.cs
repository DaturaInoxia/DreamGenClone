using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

public sealed class IdentityControlledRequestCompilerTests
{
    [Fact]
    public async Task CompileAsync_PreservesOrderedExactPackOwnershipRegionsAndProvenance()
    {
        var repository = new StubIdentityRepository();
        repository.AddApprovedPack("pack-dean", 7, "face-dean", "dean.png", "DEANHASH");
        repository.AddApprovedPack("pack-becky", 3, "face-becky", "becky.png", "BECKYHASH");
        var storage = new StubIdentityStorage(new Dictionary<string, byte[]>
        {
            ["dean.png"] = [1, 2, 3],
            ["becky.png"] = [4, 5]
        });
        var compiler = new IdentityControlledRequestCompiler(repository, storage);
        var image = new SceneImageRecord
        {
            Id = "attempt-1",
            ImageSize = "1024x1024",
            IdentityPacksJson = JsonSerializer.Serialize(new[]
            {
                new IdentityPackSelection
                {
                    PackId = "pack-dean", CharacterLabel = "Dean", Strength = 0.7,
                    Region = new SceneImageEditTargetRegion { X = 0, Y = 0, Width = 0.5, Height = 1 }
                },
                new IdentityPackSelection
                {
                    PackId = "pack-becky", CharacterLabel = "Becky", Strength = 0.6,
                    Region = new SceneImageEditTargetRegion { X = 0.5, Y = 0, Width = 0.5, Height = 1 }
                }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };

        var result = await compiler.CompileAsync(new IdentityRequestCompilationInput(image, "positive", "negative", 42));

        Assert.Equal("attempt-1", result.Request.CorrelationId);
        Assert.Equal(42, result.Request.Seed);
        Assert.Collection(result.Request.References,
            dean =>
            {
                Assert.Equal("Dean", dean.CharacterLabel);
                Assert.Equal([1, 2, 3], dean.ReferenceImageBytes);
                Assert.Equal(0.7, dean.StrengthOverride);
                Assert.Equal(0.5, dean.Region!.Width);
            },
            becky =>
            {
                Assert.Equal("Becky", becky.CharacterLabel);
                Assert.Equal([4, 5], becky.ReferenceImageBytes);
                Assert.Equal(0.6, becky.StrengthOverride);
                Assert.Equal(0.5, becky.Region!.X);
            });
        Assert.Collection(result.References,
            dean =>
            {
                Assert.Equal("pack-dean", dean.PackId);
                Assert.Equal(7, dean.PackVersion);
                Assert.Equal("face-dean", dean.FaceAssetId);
                Assert.Equal("DEANHASH", dean.ReferenceSha256);
            },
            becky =>
            {
                Assert.Equal("pack-becky", becky.PackId);
                Assert.Equal(3, becky.PackVersion);
                Assert.Equal("face-becky", becky.FaceAssetId);
                Assert.Equal("BECKYHASH", becky.ReferenceSha256);
            });
    }

    [Fact]
    public async Task CompileAsync_MalformedPersistedSelections_FailsWithoutSinglePackFallback()
    {
        var compiler = new IdentityControlledRequestCompiler(
            new StubIdentityRepository(),
            new StubIdentityStorage(new Dictionary<string, byte[]>()));
        var image = new SceneImageRecord
        {
            IdentityPackId = "fallback-pack",
            IdentityPacksJson = "{not-json"
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            compiler.CompileAsync(new IdentityRequestCompilationInput(image, "positive", "negative", 42)));

        Assert.Contains("malformed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompileAsync_CanonicalFaceOwnedByAnotherPack_FailsExplicitly()
    {
        var repository = new StubIdentityRepository();
        repository.Packs["pack-dean"] = new CharacterImageIdentityPack
        {
            Id = "pack-dean", Version = 7, Status = CharacterImageIdentityPackStatus.Approved,
            CanonicalFaceAssetId = "face-dean"
        };
        repository.Assets["face-dean"] = new SceneImageReferenceAsset
        {
            Id = "face-dean", IdentityPackId = "pack-other", AssetKind = SceneImageReferenceAssetKind.Face,
            IsApproved = true, FileRelativePath = "dean.png"
        };
        var compiler = new IdentityControlledRequestCompiler(
            repository,
            new StubIdentityStorage(new Dictionary<string, byte[]>()));
        var image = new SceneImageRecord { IdentityPackId = "pack-dean" };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            compiler.CompileAsync(new IdentityRequestCompilationInput(image, "positive", "negative", null)));

        Assert.Contains("owned", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubIdentityRepository : ICharacterImageIdentityRepository
    {
        public Dictionary<string, CharacterImageIdentityPack> Packs { get; } = [];
        public Dictionary<string, SceneImageReferenceAsset> Assets { get; } = [];

        public void AddApprovedPack(string packId, int version, string faceId, string path, string sha256)
        {
            Packs[packId] = new CharacterImageIdentityPack
            {
                Id = packId, Version = version, Status = CharacterImageIdentityPackStatus.Approved,
                CanonicalFaceAssetId = faceId
            };
            Assets[faceId] = new SceneImageReferenceAsset
            {
                Id = faceId, IdentityPackId = packId, AssetKind = SceneImageReferenceAssetKind.Face,
                IsApproved = true, FileRelativePath = path, Sha256 = sha256
            };
        }

        public Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken = default)
            => Task.FromResult(Packs.GetValueOrDefault(packId));
        public Task<SceneImageReferenceAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult(Assets.GetValueOrDefault(assetId));
        public Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(string characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

    private sealed class StubIdentityStorage(IReadOnlyDictionary<string, byte[]> files) : ICharacterImageAssetStorageService
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream(files[relativePath], writable: false));
        public Task<StoredCharacterImageAsset> SaveAsync(string characterProfileId, string fileName, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}