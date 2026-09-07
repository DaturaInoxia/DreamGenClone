using System.Text.Json;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ReferenceBootstrapRepositoryTests
{
    [Fact]
    public async Task GenerateCandidates_EnqueuesProducedImageJobsWithTypedReferencePayload()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reference-bootstrap-generation-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var queue = new CapturingBackgroundJobQueue();
        var service = new ReferenceBootstrapService(
            new ReferenceBootstrapRepository(options),
            new ProducedImageRepository(options),
            null!,
            null!,
            NullLogger<ReferenceBootstrapService>.Instance,
            null!,
            null!,
            null!,
            queue,
            new StubDurableSettingsResolver(),
            TimeProvider.System);

        try
        {
            await service.CreateBatchAsync(new ReferenceBootstrapBatch
            {
                Id = "batch-generation",
                CharacterProfileId = "character-generation",
                TargetAssetType = SceneAssetType.CharacterFace,
                Description = "A calm portrait",
                RequestedCandidateCount = 3
            });

            await service.GenerateCandidatesAsync("batch-generation");

            Assert.Equal(3, queue.Enqueued.Count);
            Assert.All(queue.Enqueued, job =>
            {
                Assert.Equal(BackgroundJobTypes.ProducedImageGeneration, job.JobType);
                Assert.Equal(DurableJobLane.ImageRender, job.Lane);
                var payload = JsonSerializer.Deserialize<ProducedImageGenerationJobPayload>(job.PayloadJson);
                Assert.NotNull(payload);
                Assert.Equal("batch-generation", payload!.BatchId);
                Assert.Equal("character-generation", payload.TargetRef);
                Assert.Equal(ProducedImageReferenceKind.CharacterFace, payload.ReferenceKind);
                Assert.Equal("A calm portrait", payload.VisionText);
            });
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(dbPath + suffix); } catch { }
            }
        }
    }

    [Fact]
    public void GenerationPayload_RoundTripsCandidateBatchId()
    {
        var payload = new SceneAssetGenerationJobPayload
        {
            AssetId = "asset-1",
            ImageId = "image-1",
            ModelId = "model-1",
            ImageSize = "1024x1024",
            CandidateBatchId = "batch-1"
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<SceneAssetGenerationJobPayload>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Equal("batch-1", roundTripped!.CandidateBatchId);
    }

    [Fact]
    public async Task BatchAndSceneAssetCandidate_RoundTrip()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reference-bootstrap-repo-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False"
        });
        var bootstrapRepository = new ReferenceBootstrapRepository(options);
        var assetRepository = new SceneAssetRepository(options);

        try
        {
            var batch = new ReferenceBootstrapBatch
            {
                Id = "batch-1",
                CharacterProfileId = "character-1",
                TargetAssetType = SceneAssetType.CharacterFace,
                Description = "A calm portrait",
                FrozenTextBlock = "Frozen character face description",
                RequestedCandidateCount = 4,
                Status = ReferenceBootstrapBatchStatus.Draft
            };
            await bootstrapRepository.UpsertBatchAsync(batch);

            var loadedBatch = await bootstrapRepository.GetBatchAsync(batch.Id);
            Assert.NotNull(loadedBatch);
            Assert.Equal(batch.Id, loadedBatch!.Id);
            Assert.Equal(batch.CharacterProfileId, loadedBatch.CharacterProfileId);
            Assert.Equal(batch.TargetAssetType, loadedBatch.TargetAssetType);
            Assert.Equal(batch.Description, loadedBatch.Description);
            Assert.Equal(batch.FrozenTextBlock, loadedBatch.FrozenTextBlock);
            Assert.Equal(batch.RequestedCandidateCount, loadedBatch.RequestedCandidateCount);

            await assetRepository.UpsertAsync(new SceneAsset
            {
                Id = "asset-1",
                Name = "Portrait candidate",
                Kind = SceneAssetKind.PromptGenerated,
                Status = SceneAssetStatus.Complete
            });
            await assetRepository.UpdateCandidateFieldsAsync(
                "asset-1",
                batch.Id,
                SceneAssetCandidateDecision.Accepted,
                "Strong identity match",
                null);

            var loadedAsset = await assetRepository.GetAsync("asset-1");
            Assert.NotNull(loadedAsset);
            Assert.Equal(batch.Id, loadedAsset!.CandidateBatchId);
            Assert.Equal(SceneAssetCandidateDecision.Accepted, loadedAsset.CandidateDecision);
            Assert.Equal("Strong identity match", loadedAsset.CandidateNotes);
            Assert.Null(loadedAsset.CandidateSourceAssetId);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(dbPath + suffix); } catch { /* best effort */ }
            }
        }
    }

    [Fact]
    public async Task CandidateCuration_ListsAndUpdatesCandidates()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reference-bootstrap-curation-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False"
        });
        var bootstrapRepository = new ReferenceBootstrapRepository(options);
        var producedImageRepository = new ProducedImageRepository(options);
        var service = new ReferenceBootstrapService(
            bootstrapRepository,
            producedImageRepository,
            null!,
            null!,
            NullLogger<ReferenceBootstrapService>.Instance,
            null!,
            null!);

        try
        {
            var batch = await service.CreateBatchAsync(new ReferenceBootstrapBatch
            {
                Id = "batch-curation",
                CharacterProfileId = "character-curation",
                Description = "A calm portrait",
                RequestedCandidateCount = 1,
                TargetAssetType = SceneAssetType.CharacterFace
            });
            await producedImageRepository.InsertAsync(new ProducedImage
            {
                Id = "candidate-1",
                Kind = ProducedImageKind.ReferenceCandidate,
                BatchId = batch.Id,
                Status = ProducedImageStatus.Undecided
            });

            var candidates = await service.ListCandidatesAsync(batch.Id);
            Assert.Single(candidates);
            Assert.Equal("candidate-1", candidates[0].Id);

            await service.SetCandidateDecisionAsync(
                "candidate-1",
                ProducedImageStatus.Shortlisted,
                "looks good");

            var updated = await producedImageRepository.GetAsync("candidate-1");
            Assert.NotNull(updated);
            Assert.Equal(ProducedImageStatus.Shortlisted, updated!.Status);
            Assert.Equal("looks good", updated.CandidateNotes);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(dbPath + suffix); } catch { /* best effort */ }
            }
        }
    }

    [Fact]
    public async Task SetCandidateDecision_MissingAsset_Throws()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reference-bootstrap-missing-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False"
        });
        var service = new ReferenceBootstrapService(
            new ReferenceBootstrapRepository(options),
            new ProducedImageRepository(options),
            null!,
            null!,
            NullLogger<ReferenceBootstrapService>.Instance,
            null!,
            null!);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetCandidateDecisionAsync(
                "missing-asset",
                ProducedImageStatus.Accepted,
                null));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(dbPath + suffix); } catch { /* best effort */ }
            }
        }
    }

    [Fact]
    public async Task PromoteAcceptedCharacterFace_EmptyFrozenTextBlock_Throws()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reference-bootstrap-promotion-frozen-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var bootstrapRepository = new ReferenceBootstrapRepository(options);
        var producedImageRepository = new ProducedImageRepository(options);
        var service = new ReferenceBootstrapService(
            bootstrapRepository,
            producedImageRepository,
            null!,
            null!,
            NullLogger<ReferenceBootstrapService>.Instance,
            null!,
            null!);

        try
        {
            await bootstrapRepository.UpsertBatchAsync(new ReferenceBootstrapBatch
            {
                Id = "batch-frozen-required",
                CharacterProfileId = "character-1",
                TargetAssetType = SceneAssetType.CharacterFace,
                Description = "A face",
                RequestedCandidateCount = 1
            });
            await producedImageRepository.InsertAsync(new ProducedImage
            {
                Id = "candidate-1",
                Kind = ProducedImageKind.ReferenceCandidate,
                BatchId = "batch-frozen-required",
                Status = ProducedImageStatus.Undecided
            });
            await service.SetCandidateDecisionAsync("candidate-1", ProducedImageStatus.Accepted, null);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PromoteAcceptedCharacterFaceAsync("batch-frozen-required", "candidate-1"));

            Assert.Contains("frozen text block", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(dbPath + suffix); } catch { }
            }
        }
    }

    [Fact]
    public async Task PromoteAcceptedCharacterFace_NonAcceptedCandidate_Throws()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reference-bootstrap-promotion-decision-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var bootstrapRepository = new ReferenceBootstrapRepository(options);
        var producedImageRepository = new ProducedImageRepository(options);
        var service = new ReferenceBootstrapService(
            bootstrapRepository,
            producedImageRepository,
            null!,
            null!,
            NullLogger<ReferenceBootstrapService>.Instance,
            null!,
            null!);

        try
        {
            await bootstrapRepository.UpsertBatchAsync(new ReferenceBootstrapBatch
            {
                Id = "batch-decision-required",
                CharacterProfileId = "character-1",
                TargetAssetType = SceneAssetType.CharacterFace,
                Description = "A face",
                FrozenTextBlock = "Frozen description",
                RequestedCandidateCount = 1
            });
            await producedImageRepository.InsertAsync(new ProducedImage
            {
                Id = "candidate-undecided",
                Kind = ProducedImageKind.ReferenceCandidate,
                BatchId = "batch-decision-required",
                Status = ProducedImageStatus.Undecided
            });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PromoteAcceptedCharacterFaceAsync("batch-decision-required", "candidate-undecided"));

            Assert.Contains("not Accepted", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(dbPath + suffix); } catch { }
            }
        }
    }

    [Fact]
    public async Task PromoteAcceptedCharacterFace_MissingAsset_Throws()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reference-bootstrap-promotion-missing-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var bootstrapRepository = new ReferenceBootstrapRepository(options);
        var service = new ReferenceBootstrapService(
            bootstrapRepository,
            new ProducedImageRepository(options),
            null!,
            null!,
            NullLogger<ReferenceBootstrapService>.Instance,
            null!,
            null!);

        try
        {
            await bootstrapRepository.UpsertBatchAsync(new ReferenceBootstrapBatch
            {
                Id = "batch-asset-required",
                CharacterProfileId = "character-1",
                TargetAssetType = SceneAssetType.CharacterFace,
                Description = "A face",
                FrozenTextBlock = "Frozen description",
                RequestedCandidateCount = 1
            });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PromoteAcceptedCharacterFaceAsync("batch-asset-required", "missing-candidate"));

            Assert.Contains("was not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(dbPath + suffix); } catch { }
            }
        }
    }

    [Theory]
    [InlineData(SceneAssetType.CharacterBody)]
    [InlineData(SceneAssetType.Wardrobe)]
    [InlineData(SceneAssetType.Location)]
    public async Task PromoteAcceptedReference_EmptyFrozenTextBlock_Throws(SceneAssetType targetAssetType)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reference-bootstrap-promotion-empty-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var bootstrapRepository = new ReferenceBootstrapRepository(options);
        var producedImageRepository = new ProducedImageRepository(options);
        var service = new ReferenceBootstrapService(
            bootstrapRepository, producedImageRepository, null!, null!,
            NullLogger<ReferenceBootstrapService>.Instance, null!, null!);

        try
        {
            await bootstrapRepository.UpsertBatchAsync(new ReferenceBootstrapBatch
            {
                Id = "batch-empty-frozen",
                CharacterProfileId = targetAssetType == SceneAssetType.Location ? null : "character-1",
                LocationProfileId = targetAssetType == SceneAssetType.Location ? "location-1" : null,
                TargetAssetType = targetAssetType == SceneAssetType.Location ? null : targetAssetType,
                Description = "A reference",
                RequestedCandidateCount = 1
            });
            await producedImageRepository.InsertAsync(new ProducedImage
            {
                Id = "candidate-empty-frozen",
                Kind = ProducedImageKind.ReferenceCandidate,
                BatchId = "batch-empty-frozen",
                Status = ProducedImageStatus.Accepted
            });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PromoteAsync(service, targetAssetType, "batch-empty-frozen", "candidate-empty-frozen"));

            Assert.Contains("frozen text block", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDatabaseFiles(dbPath);
        }
    }

    [Theory]
    [InlineData(SceneAssetType.CharacterBody)]
    [InlineData(SceneAssetType.Wardrobe)]
    [InlineData(SceneAssetType.Location)]
    public async Task PromoteAcceptedReference_NonAcceptedCandidate_Throws(SceneAssetType targetAssetType)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reference-bootstrap-promotion-nonaccepted-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var bootstrapRepository = new ReferenceBootstrapRepository(options);
        var producedImageRepository = new ProducedImageRepository(options);
        var service = new ReferenceBootstrapService(
            bootstrapRepository, producedImageRepository, null!, null!,
            NullLogger<ReferenceBootstrapService>.Instance, null!, null!);

        try
        {
            await bootstrapRepository.UpsertBatchAsync(new ReferenceBootstrapBatch
            {
                Id = "batch-nonaccepted",
                CharacterProfileId = targetAssetType == SceneAssetType.Location ? null : "character-1",
                LocationProfileId = targetAssetType == SceneAssetType.Location ? "location-1" : null,
                TargetAssetType = targetAssetType == SceneAssetType.Location ? null : targetAssetType,
                Description = "A reference",
                FrozenTextBlock = "Frozen description",
                RequestedCandidateCount = 1
            });
            await producedImageRepository.InsertAsync(new ProducedImage
            {
                Id = "candidate-nonaccepted",
                Kind = ProducedImageKind.ReferenceCandidate,
                BatchId = "batch-nonaccepted",
                Status = ProducedImageStatus.Undecided
            });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PromoteAsync(service, targetAssetType, "batch-nonaccepted", "candidate-nonaccepted"));

            Assert.Contains("not Accepted", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDatabaseFiles(dbPath);
        }
    }

    [Theory]
    [InlineData(SceneAssetType.CharacterBody)]
    [InlineData(SceneAssetType.Wardrobe)]
    [InlineData(SceneAssetType.Location)]
    public async Task PromoteAcceptedReference_MissingAsset_Throws(SceneAssetType targetAssetType)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"reference-bootstrap-promotion-missing-reference-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var bootstrapRepository = new ReferenceBootstrapRepository(options);
        var service = new ReferenceBootstrapService(
            bootstrapRepository, new ProducedImageRepository(options), null!, null!,
            NullLogger<ReferenceBootstrapService>.Instance, null!, null!);

        try
        {
            await bootstrapRepository.UpsertBatchAsync(new ReferenceBootstrapBatch
            {
                Id = "batch-missing-reference",
                CharacterProfileId = targetAssetType == SceneAssetType.Location ? null : "character-1",
                LocationProfileId = targetAssetType == SceneAssetType.Location ? "location-1" : null,
                TargetAssetType = targetAssetType == SceneAssetType.Location ? null : targetAssetType,
                Description = "A reference",
                FrozenTextBlock = "Frozen description",
                RequestedCandidateCount = 1
            });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PromoteAsync(service, targetAssetType, "batch-missing-reference", "missing-candidate"));

            Assert.Contains("was not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDatabaseFiles(dbPath);
        }
    }

    private static Task PromoteAsync(
        ReferenceBootstrapService service,
        SceneAssetType targetAssetType,
        string batchId,
        string producedImageId) => targetAssetType switch
        {
            SceneAssetType.CharacterBody => service.PromoteAcceptedCharacterBodyAsync(batchId, producedImageId),
            SceneAssetType.Wardrobe => service.PromoteAcceptedWardrobeAsync(batchId, producedImageId),
            SceneAssetType.Location => service.PromoteAcceptedLocationAsync(batchId, producedImageId),
            _ => throw new ArgumentOutOfRangeException(nameof(targetAssetType), targetAssetType, null)
        };

    private static void DeleteDatabaseFiles(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(dbPath + suffix); } catch { }
        }
    }

    private sealed class CapturingBackgroundJobQueue : IDurableBackgroundJobQueue
    {
        public List<DurableBackgroundJob> Enqueued { get; } = [];

        public Task<bool> TryEnqueueAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(job);
            return Task.FromResult(true);
        }

        public Task<DurableBackgroundJob?> GetAsync(string jobId, CancellationToken cancellationToken = default)
            => Task.FromResult<DurableBackgroundJob?>(Enqueued.SingleOrDefault(job => job.Id == jobId));

        public Task<bool> TryActivateAsync(string jobId, DateTime activatedUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(Enqueued.Any(job => job.Id == jobId));

        public Task<bool> TryCancelAsync(string jobId, DateTime cancelledUtc, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task WaitForWorkAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubDurableSettingsResolver : ISceneBeatAnalyzerResolver
    {
        public Task<ResolvedSceneBeatAnalyzer> ResolveAsync(CancellationToken cancellationToken = default)
        {
            var model = new ResolvedModel(
                "https://example.test",
                "/chat",
                30,
                null,
                "model",
                0.2,
                0.8,
                1024,
                "provider",
                false);
            return Task.FromResult(new ResolvedSceneBeatAnalyzer(
                "function",
                "model",
                "provider",
                model,
                StructuredOutputMode.StrictJsonSchema,
                4096,
                1024,
                1,
                120,
                250,
                [5, 30],
                30,
                8));
        }
    }
}