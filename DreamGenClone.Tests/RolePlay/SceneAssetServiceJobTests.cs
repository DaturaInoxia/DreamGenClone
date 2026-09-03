using System.Text.Json;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Infrastructure.Storage;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneAssetServiceJobTests
{
    [Fact]
    public void AssetPromptCompiler_UsesSelectedModelFamilyWithoutChangingSemanticInput()
    {
        var pony = Model(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags);
        var sdxl = Model(SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage);
        const string description = "A detective in a rain-soaked alley.";

        var ponyCompilation = SceneAssetPromptCompiler.Compile(
            description, SceneAssetType.CharacterBody, pony);
        var sdxlCompilation = SceneAssetPromptCompiler.Compile(
            description, SceneAssetType.CharacterBody, sdxl);

        Assert.StartsWith(
            "score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, 1person,",
            ponyCompilation.Prompt);
        Assert.Contains("A detective in a rain-soaked alley", ponyCompilation.Prompt);
        Assert.Equal(description, sdxlCompilation.Prompt);
        Assert.Equal("scene-asset-pony-v6", ponyCompilation.CompilerId);
        Assert.Equal("scene-asset-sdxl-natural-language", sdxlCompilation.CompilerId);
    }

    private sealed class CapturingBackgroundJobQueue : IBackgroundJobQueue
    {
        public List<(string JobType, string PayloadJson, string? DedupeKey)> Enqueued { get; } = [];

        public bool Enqueue(string jobType, string payloadJson, string? dedupeKey = null)
        {
            Enqueued.Add((jobType, payloadJson, dedupeKey));
            return true;
        }

        public ValueTask<BackgroundJobEnvelope> DequeueAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used in this test.");
        public void MarkProcessing(string jobId) { }
        public void MarkCompleted(string jobId) { }
        public void MarkFailed(string jobId, string errorMessage) { }
    }

    private static (SceneAssetService service, CapturingBackgroundJobQueue queue, SceneAssetRepository repo, SceneAssetStorageService storage, string dbPath, string root)
        Build()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"scene-asset-svc-{Guid.NewGuid():N}.db");
        var root = Path.Combine(Path.GetTempPath(), $"scene-asset-svc-files-{Guid.NewGuid():N}");
        var persistenceOptions = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False", SceneImageRoot = root });
        var repo = new SceneAssetRepository(persistenceOptions);
        var storage = new SceneAssetStorageService(persistenceOptions, NullLogger<SceneAssetStorageService>.Instance);
        var queue = new CapturingBackgroundJobQueue();
        var service = new SceneAssetService(repo, storage, queue, NullLogger<SceneAssetService>.Instance);
        return (service, queue, repo, storage, dbPath, root);
    }

    [Fact]
    public async Task CreateFromPrompt_EnqueuesGenerationJob()
    {
        var (service, queue, repo, _, dbPath, root) = Build();
        try
        {
            var asset = await service.CreateFromPromptAsync(
                "Forest", "a misty forest clearing", SceneAssetType.Location, "model-42", "1024x1024");

            Assert.Equal(SceneAssetKind.PromptGenerated, asset.Kind);
            Assert.Equal(SceneAssetStatus.Pending, asset.Status);
            Assert.Equal(SceneAssetType.Location, asset.Type);
            Assert.Equal("a misty forest clearing", asset.Prompt);
            Assert.Single(queue.Enqueued);
            Assert.Equal(BackgroundJobTypes.SceneAssetGeneration, queue.Enqueued[0].JobType);
            Assert.Equal($"{BackgroundJobTypes.SceneAssetGeneration}:{asset.Id}", queue.Enqueued[0].DedupeKey);
            var payload = JsonSerializer.Deserialize<SceneAssetGenerationJobPayload>(
                queue.Enqueued[0].PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(payload);
            Assert.Equal(asset.Id, payload.AssetId);
            Assert.Equal("model-42", payload.ModelId);
            Assert.Equal("1024x1024", payload.ImageSize);
            Assert.NotNull(await repo.GetAsync(asset.Id));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task CreateFromPrompt_RequiresNonEmptyPrompt()
    {
        var (service, _, _, _, dbPath, root) = Build();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateFromPromptAsync(
                "X", "   ", SceneAssetType.Prop, "model-42", "1024x1024"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateFromPromptAsync(
                "X", "a brass key", SceneAssetType.Prop, "   ", "1024x1024"));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task CreateFromUpload_PersistsCompleteAsset()
    {
        var (service, queue, repo, _, dbPath, root) = Build();
        try
        {
            var png = MinimalPng();
            await using var stream = new MemoryStream(png);
            var asset = await service.CreateFromUploadAsync("Photo", SceneAssetType.Style, "photo.png", stream);

            Assert.Equal(SceneAssetKind.Uploaded, asset.Kind);
            Assert.Equal(SceneAssetStatus.Complete, asset.Status);
            Assert.Equal(SceneAssetType.Style, asset.Type);
            Assert.StartsWith("assets/", asset.FileRelativePath);
            Assert.Equal(png.Length, asset.ByteLength);
            Assert.Equal("image/png", asset.MediaType);
            Assert.Empty(queue.Enqueued);
            Assert.Equal(SceneAssetStatus.Complete, (await repo.GetAsync(asset.Id))!.Status);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task ApproveForProduction_PersistsExplicitGovernanceWithoutDefaults()
    {
        var (service, _, repo, _, dbPath, root) = Build();
        try
        {
            await using var stream = new MemoryStream(MinimalPng());
            var asset = await service.CreateFromUploadAsync(
                "Dean face", SceneAssetType.CharacterFace, "dean.png", stream);

            var approved = await service.ApproveForProductionAsync(
                asset.Id,
                "{\"source\":\"curator upload\"}",
                SceneAssetConsentState.Confirmed,
                SceneAssetLicenseState.Confirmed,
                "owned reference",
                SceneAssetApprovedUseScope.CharacterIdentity,
                "local-adult-production",
                "{\"families\":[\"sdxl\"]}");

            Assert.Equal(SceneAssetProductionApprovalStatus.Approved, approved.ProductionApprovalStatus);
            Assert.Equal(1, approved.ProductionVersion);
            Assert.Equal(SceneAssetApprovedUseScope.CharacterIdentity, approved.ApprovedUseScope);
            Assert.Equal("local-adult-production", approved.ContentPolicyKey);
            Assert.Equal("owned reference", approved.LicenseLabel);
            Assert.Equal(approved.ProductionApprovalStatus, (await repo.GetAsync(asset.Id))!.ProductionApprovalStatus);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueEdit_RequiresCompleteSource()
    {
        var (service, _, repo, _, dbPath, root) = Build();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueEditAsync(
                "missing", "Edit", "change lighting", "editor-1"));

            var pending = new SceneAsset { Id = "p1", Name = "Pending", Kind = SceneAssetKind.PromptGenerated, Status = SceneAssetStatus.Pending };
            await repo.UpsertAsync(pending);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueEditAsync(
                "p1", "Edit", "change lighting", "editor-1"));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueEdit_EnqueuesEditJob()
    {
        var (service, queue, repo, _, dbPath, root) = Build();
        try
        {
            var source = new SceneAsset { Id = "s1", Name = "Source", Kind = SceneAssetKind.PromptGenerated, Status = SceneAssetStatus.Complete, Type = SceneAssetType.Wardrobe, FileRelativePath = "assets/s1.png" };
            await repo.UpsertAsync(source);

            var asset = await service.EnqueueEditAsync("s1", "Edited", "change lighting", "editor-7");

            Assert.Equal(SceneAssetKind.Edited, asset.Kind);
            Assert.Equal(SceneAssetStatus.Pending, asset.Status);
            Assert.Equal(SceneAssetType.Wardrobe, asset.Type);
            Assert.Equal("s1", asset.SourceAssetId);
            Assert.Single(queue.Enqueued);
            Assert.Equal(BackgroundJobTypes.SceneAssetEditing, queue.Enqueued[0].JobType);
            var payload = JsonSerializer.Deserialize<SceneAssetEditingJobPayload>(
                queue.Enqueued[0].PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(payload);
            Assert.Equal(asset.Id, payload.AssetId);
            Assert.Equal("editor-7", payload.ModelId);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueProfilePack_ValidatesInputs()
    {
        var (service, _, _, _, dbPath, root) = Build();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.EnqueueProfilePackAsync(new SceneAssetProfilePackJobPayload { CharacterProfileId = "" }));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.EnqueueProfilePackAsync(new SceneAssetProfilePackJobPayload { CharacterProfileId = "char-1" }));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.EnqueueProfilePackAsync(new SceneAssetProfilePackJobPayload
                {
                    CharacterProfileId = "char-1",
                    Description = "blonde woman",
                    EditorModelId = "editor-1"
                }));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueProfilePack_EnqueuesDeduplicatedPackJob()
    {
        var (service, queue, _, _, dbPath, root) = Build();
        try
        {
            await service.EnqueueProfilePackAsync(new SceneAssetProfilePackJobPayload
            {
                CharacterProfileId = "char-1",
                CharacterName = "Becky",
                Description = "blonde woman",
                FrontModelId = "generator-1",
                EditorModelId = "editor-1"
            });

            Assert.Single(queue.Enqueued);
            Assert.Equal(BackgroundJobTypes.SceneAssetProfilePackGeneration, queue.Enqueued[0].JobType);
            Assert.Equal($"{BackgroundJobTypes.SceneAssetProfilePackGeneration}:char-1", queue.Enqueued[0].DedupeKey);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueProfilePack_PayloadRoundTripsThroughHandlerContract()
    {
        var (service, queue, _, _, dbPath, root) = Build();
        try
        {
            await service.EnqueueProfilePackAsync(new SceneAssetProfilePackJobPayload
            {
                CharacterProfileId = "faee1ec0-1cf3-459e-97d2-ad59717c41ba",
                CharacterName = "Dean",
                FrontAssetId = "ce09a98859914aa985d205b814723ca9",
                EditorModelId = "editor-1"
            });

            var json = Assert.Single(queue.Enqueued).PayloadJson;
            // The service serializes with JsonSerializerDefaults.Web (camelCase). The profile-pack
            // handler must deserialize with the same Web options; case-sensitive default matching
            // silently drops CharacterProfileId (regression guard for that exact bug).
            Assert.Contains("\"characterProfileId\"", json);

            var roundTripped = JsonSerializer.Deserialize<SceneAssetProfilePackJobPayload>(
                json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(roundTripped);
            Assert.Equal("faee1ec0-1cf3-459e-97d2-ad59717c41ba", roundTripped.CharacterProfileId);
            Assert.Equal("ce09a98859914aa985d205b814723ca9", roundTripped.FrontAssetId);
            Assert.Equal("editor-1", roundTripped.EditorModelId);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task DeleteAsset_RefusesIdentityPackAssets()
    {
        var (service, _, repo, _, dbPath, root) = Build();
        try
        {
            var packAsset = new SceneAsset { Id = "pa1", Name = "Face", Kind = SceneAssetKind.ProfilePackFace, Status = SceneAssetStatus.Complete, IdentityPackId = "pack-1", FileRelativePath = "identity/c/f.png" };
            await repo.UpsertAsync(packAsset);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAssetAsync("pa1"));
            Assert.NotNull(await repo.GetAsync("pa1"));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task DeleteAsset_DeletesRowAndFileWhenUnreferenced()
    {
        var (service, _, repo, storage, dbPath, root) = Build();
        try
        {
            var png = MinimalPng();
            await using (var stream = new MemoryStream(png))
            {
                await service.CreateFromUploadAsync("Photo", SceneAssetType.Location, "photo.png", stream);
            }
            var asset = (await repo.ListAsync()).Single();
            var fullPath = Path.Combine(root, asset.FileRelativePath!);
            Assert.True(File.Exists(fullPath));

            await service.DeleteAssetAsync(asset.Id);

            Assert.Null(await repo.GetAsync(asset.Id));
            Assert.False(File.Exists(fullPath));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task OpenForDownload_RequiresCompleteAsset()
    {
        var (service, _, repo, _, dbPath, root) = Build();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenForDownloadAsync("missing"));

            var pending = new SceneAsset { Id = "p1", Name = "Pending", Kind = SceneAssetKind.PromptGenerated, Status = SceneAssetStatus.Pending };
            await repo.UpsertAsync(pending);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenForDownloadAsync("p1"));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    private static void Cleanup(string dbPath, string root)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(dbPath + suffix); } catch { /* best effort */ }
        }
        try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
    }

    private static byte[] MinimalPng()
    {
        var bytes = new byte[29];
        bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;
        bytes[4] = 0x0D; bytes[5] = 0x0A; bytes[6] = 0x1A; bytes[7] = 0x0A;
        bytes[8] = 0; bytes[9] = 0; bytes[10] = 0; bytes[11] = 13;
        bytes[12] = 0x49; bytes[13] = 0x48; bytes[14] = 0x44; bytes[15] = 0x52;
        bytes[16] = 0; bytes[17] = 0; bytes[18] = 0; bytes[19] = 16;
        bytes[20] = 0; bytes[21] = 0; bytes[22] = 0; bytes[23] = 16;
        bytes[24] = 8; bytes[25] = 6; bytes[26] = 0; bytes[27] = 0; bytes[28] = 0;
        return bytes;
    }

    private static ResolvedImageModel Model(
        SceneImageModelFamily family,
        SceneImagePromptDialect dialect) => new(
            "https://example.test",
            "/images",
            60,
            null,
            "model",
            ImageContentPolicy.AdultAllowed,
            "provider",
            false,
            family,
            dialect,
            ImageProtocol.OpenAiImages);
}
