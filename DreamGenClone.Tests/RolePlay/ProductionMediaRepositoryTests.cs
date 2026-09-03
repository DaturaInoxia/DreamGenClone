using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ProductionMediaRepositoryTests
{
    private const string Sha256 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task SchemaAndImmutableGraph_RoundTripExactBindings()
    {
        await using var fixture = await Fixture.CreateAsync();
        var graph = await fixture.CreateCompiledGraphAsync();

        var intent = await fixture.Repository.GetIntentAsync(graph.Intent.Id);
        var request = await fixture.Repository.GetCompiledRequestAsync(graph.Request.Id);
        var bindings = await fixture.Repository.ListReferenceBindingsAsync(graph.Request.Id);

        Assert.Equal(graph.Intent.ContentHash, intent!.ContentHash);
        Assert.Equal(graph.Request.ContentHash, request!.ContentHash);
        Assert.Equal(graph.Asset.Id, Assert.Single(bindings).SceneAssetId);
        Assert.Equal(Sha256, bindings[0].SceneAssetSha256);
    }

    [Fact]
    public async Task CapabilityProfiles_ListExactPersistedProfiles()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.CreateCapabilityAsync();

        var profiles = await fixture.Repository.ListCapabilityProfilesAsync();

        var profile = Assert.Single(profiles);
        Assert.Equal(created.Profile.Id, profile.Id);
        Assert.Equal(created.Profile.EvidenceRunId, profile.EvidenceRunId);
        Assert.Equal(MediaCapabilityProfileStatus.Qualified, profile.Status);
    }

    [Fact]
    public async Task CharacterAssetIntent_RoundTripsWithoutFabricatedSceneLineage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intent = new ProductionIntentSnapshot
        {
            Id = "character-intent-1", ContextKind = ProductionContextKind.CharacterAsset,
            ContextId = "dataset-1",
            ContextSnapshotJson = "{\"datasetId\":\"dataset-1\",\"characterProfileId\":\"character-1\",\"identityPackId\":\"pack-1\",\"candidateKind\":\"identity-seed\",\"coverageKey\":\"front-close\",\"assetName\":\"Front seed\",\"assetType\":\"CharacterFace\"}",
            Pov = "Dean", Operation = MediaOperation.Generate, VisibleActorsJson = "[]",
            CompositionIntentJson = "{}", CameraIntentJson = "{}", StyleIntentJson = "{}",
            PreservationConstraintsJson = "{}", ChangeIntentJson = "{}", ContentPolicyJson = "{}",
            CreatedUtc = DateTime.UtcNow
        };
        intent.ContentHash = ProductionContentHash.ForIntent(intent);

        await fixture.Repository.CreateIntentAsync(intent);
        var loaded = await fixture.Repository.GetIntentAsync(intent.Id);

        Assert.NotNull(loaded);
        Assert.Equal(ProductionContextKind.CharacterAsset, loaded.ContextKind);
        Assert.Equal("dataset-1", loaded.ContextId);
        Assert.Empty(loaded.SessionId);
        Assert.Empty(loaded.ProductionGroupId);

        intent.SessionId = "fabricated-session";
        intent.ContentHash = ProductionContentHash.ForIntent(intent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository.CreateIntentAsync(intent));
    }

    [Fact]
    public async Task LegacySceneIntentSchema_MigratesPayloadToExplicitContext()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"production-media-legacy-{Guid.NewGuid():N}.db");
        try
        {
            var legacyIntent = new ProductionIntentSnapshot
            {
                Id = "legacy-intent-1", ProductionGroupId = "group-1", SessionId = "session-1",
                CatalogueId = "catalogue-1", BeatId = "beat-1", BeatProductionPlanId = "plan-1",
                BeatProductionPlanVersion = 1, MomentSetId = "set-1", MomentSetVersion = 1,
                MomentId = "moment-1", MomentEnrichmentId = "enrichment-1", MomentEnrichmentRevision = 1,
                Pov = "Dean", Operation = MediaOperation.Generate, VisibleActorsJson = "[]",
                CompositionIntentJson = "{}", CameraIntentJson = "{}", StyleIntentJson = "{}",
                PreservationConstraintsJson = "{}", ChangeIntentJson = "{}", ContentPolicyJson = "{}",
                CreatedUtc = DateTime.UtcNow
            };
            legacyIntent.ContentHash = ProductionContentHash.ForIntent(legacyIntent);
            await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE SceneImageProductionGroups (Id TEXT PRIMARY KEY);
                    INSERT INTO SceneImageProductionGroups (Id) VALUES ('group-1');
                    CREATE TABLE ProductionIntentSnapshots (
                        Id TEXT PRIMARY KEY, ProductionGroupId TEXT NOT NULL, SessionId TEXT NOT NULL,
                        CatalogueId TEXT NOT NULL, BeatId TEXT NOT NULL, BeatProductionPlanId TEXT NOT NULL,
                        BeatProductionPlanVersion INTEGER NOT NULL, MomentSetId TEXT NOT NULL,
                        MomentSetVersion INTEGER NOT NULL, MomentId TEXT NOT NULL,
                        MomentEnrichmentId TEXT NOT NULL, MomentEnrichmentRevision INTEGER NOT NULL,
                        Pov TEXT NOT NULL, Operation TEXT NOT NULL, ContentHash TEXT NOT NULL,
                        PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
                        FOREIGN KEY (ProductionGroupId) REFERENCES SceneImageProductionGroups(Id)
                    );
                    INSERT INTO ProductionIntentSnapshots VALUES
                        ('legacy-intent-1', 'group-1', 'session-1', 'catalogue-1', 'beat-1', 'plan-1',
                         1, 'set-1', 1, 'moment-1', 'enrichment-1', 1, 'Dean', 'Generate',
                         $hash, $payload, $created);
                    """;
                command.Parameters.AddWithValue("$hash", legacyIntent.ContentHash);
                command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(legacyIntent, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                command.Parameters.AddWithValue("$created", legacyIntent.CreatedUtc.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

            var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
            var repository = new ProductionMediaRepository(options);
            var loaded = await repository.GetIntentAsync(legacyIntent.Id);

            Assert.NotNull(loaded);
            Assert.Equal(ProductionContextKind.SceneMoment, loaded.ContextKind);
            Assert.Equal("session-1", loaded.ContextId);
            Assert.Equal("{}", loaded.ContextSnapshotJson);
            Assert.Equal(legacyIntent.ContentHash, loaded.ContentHash);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(dbPath + suffix); } catch { }
        }
    }

    [Fact]
    public async Task IntentAndRequest_RejectMismatchedContentHashes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intent = fixture.Intent();
        intent.ContentHash = Sha256;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository.CreateIntentAsync(intent));

        intent.ContentHash = ProductionContentHash.ForIntent(intent);
        await fixture.Repository.CreateIntentAsync(intent);
        var profile = await fixture.CreateCapabilityAsync();
        var binding = fixture.Binding("request-1", fixture.Asset);
        var request = fixture.Request(intent, profile.Profile, profile.Cell);
        request.ContentHash = Sha256;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.CreateCompiledRequestAsync(request, [binding]));
    }

    [Fact]
    public async Task WorkloadTransitions_AreMonotonicAndOptimisticallyConcurrent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var graph = await fixture.CreateWorkloadGraphAsync();

        var validating = await fixture.Repository.TransitionWorkloadAsync(
            graph.Workload.Id, ProductionWorkloadStatus.Draft, ProductionWorkloadStatus.Validating, 1);
        Assert.Equal(2, validating.ConcurrencyVersion);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository.TransitionWorkloadAsync(
            graph.Workload.Id, ProductionWorkloadStatus.Draft, ProductionWorkloadStatus.Cancelled, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository.TransitionWorkloadAsync(
            graph.Workload.Id, ProductionWorkloadStatus.Validating, ProductionWorkloadStatus.Complete, 2));
    }

    [Fact]
    public async Task ListWorkloadsBySession_ReturnsLiveStateForContextRestoration()
    {
        await using var fixture = await Fixture.CreateAsync();
        var graph = await fixture.CreateWorkloadGraphAsync();
        await fixture.Repository.TransitionWorkloadAsync(
            graph.Workload.Id, ProductionWorkloadStatus.Draft, ProductionWorkloadStatus.Validating, 1);

        var workloads = await fixture.Repository.ListWorkloadsBySessionAsync("session-1");

        var loaded = Assert.Single(workloads);
        Assert.Equal(graph.Workload.Id, loaded.Id);
        Assert.Equal(ProductionWorkloadStatus.Validating, loaded.Status);
        Assert.Equal(2, loaded.ConcurrencyVersion);
        Assert.Empty(await fixture.Repository.ListWorkloadsBySessionAsync("other-session"));
    }

    [Fact]
    public async Task AttemptSubmission_IsIdempotentAndLateResultCannotOverwriteSuccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        var graph = await fixture.CreateWorkloadGraphAsync();
        var attempt = fixture.Attempt(graph.Item, graph.Compiled.Request);
        await fixture.Repository.CreateAttemptAsync(attempt);

        var submitted = await fixture.Repository.RecordProviderSubmissionAsync(
            attempt.Id, "runpod", "job-1", "https://provider.invalid/job-1", 1);
        var duplicate = await fixture.Repository.RecordProviderSubmissionAsync(
            attempt.Id, "runpod", "job-1", "https://provider.invalid/job-1", 1);
        Assert.Equal(submitted.ConcurrencyVersion, duplicate.ConcurrencyVersion);

        var running = await fixture.Repository.TransitionAttemptAsync(
            attempt.Id, ProductionAttemptStatus.Submitted, ProductionAttemptStatus.Running, submitted.ConcurrencyVersion);
        var succeeded = await fixture.Repository.RecordAttemptResultAsync(
            attempt.Id, "production/output.png", Sha256, 100, "{\"width\":1024}",
            "{\"status\":\"complete\"}", "{\"usd\":0.1}", running.ConcurrencyVersion);
        Assert.Equal(ProductionAttemptStatus.Succeeded, succeeded.Status);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository.RecordAttemptResultAsync(
            attempt.Id, "production/late.png", Sha256, 100, "{}", "{}", "{}", succeeded.ConcurrencyVersion));
    }

    [Fact]
    public async Task AttemptCreation_RejectsSecretBearingSnapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var graph = await fixture.CreateWorkloadGraphAsync();
        var attempt = fixture.Attempt(graph.Item, graph.Compiled.Request);
        attempt.RequestSnapshotJson = "{\"apiKey\":\"must-not-persist\"}";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.CreateAttemptAsync(attempt));
        Assert.Contains("secret field", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompilationService_UsesExactQualifiedCellAndPersistsCanonicalRequest()
    {
        await using var fixture = await Fixture.CreateAsync();
        var capability = await fixture.CreateCapabilityAsync();
        capability.Profile.CompilerId = "sdxl-photographic";
        capability.Profile.CompilerVersion = "1";
        var persistedProfile = new MediaCapabilityProfile
        {
            Id = "sdxl-profile", ProviderKey = capability.Profile.ProviderKey,
            ModelId = "juggernautXL_ragnarok.safetensors", ModelVersion = capability.Profile.ModelVersion,
            Operation = MediaOperation.Generate, CompilerId = "sdxl-photographic", CompilerVersion = "1",
            WorkflowRevision = capability.Profile.WorkflowRevision, NodeRevision = capability.Profile.NodeRevision,
            ArtifactManifestJson = "{}", SettingsSchemaJson = "{}", ReferenceLayoutJson = "{}",
            ControlLayoutJson = "{}", ContentPolicyKey = capability.Profile.ContentPolicyKey,
            Status = MediaCapabilityProfileStatus.Qualified, Enabled = true,
            EvidenceRunId = capability.Profile.EvidenceRunId, CreatedUtc = DateTime.UtcNow
        };
        await fixture.Repository.CreateCapabilityProfileAsync(persistedProfile);
        var cell = new MediaCapabilityCell
        {
            Id = "sdxl-cell", CapabilityProfileId = persistedProfile.Id, ActorCount = 1,
            FaceAngleKey = "front", CropKey = "medium", PoseClassKey = "portrait",
            CompositionClassKey = "single", ReferenceControlTupleJson = "{}",
            Status = MediaCapabilityCellStatus.Qualified, EvidenceRunId = "proof-1", CreatedUtc = DateTime.UtcNow
        };
        await fixture.Repository.AddCapabilityCellAsync(cell);
        var intent = fixture.Intent();
        intent.VisibleActorsJson = "[{\"description\":\"a woman\",\"clothing\":\"yellow dress\"}]";
        intent.ContentHash = ProductionContentHash.ForIntent(intent);
        await fixture.Repository.CreateIntentAsync(intent);
        var binding = fixture.Binding("compiled-by-service", fixture.Asset);
        var service = new ProductionMediaCompilationService(
            fixture.Repository,
            new ProductionMediaCompilerRegistry([new SdxlProductionMediaCompiler()]),
            fixture.LoraRepository,
            fixture.ModelRepository);

        var result = await service.CompileAndPersistAsync(
            "compiled-by-service", intent.Id, persistedProfile.Id, cell.Id,
            "{\"negativePrompt\":\"deformed\",\"width\":1024,\"height\":1024,\"steps\":30,\"guidance\":5,\"sampler\":\"dpmpp_2m_sde\",\"scheduler\":\"karras\",\"seed\":42}",
            [binding], DateTime.UtcNow);

        Assert.Equal(result.Request.ContentHash,
            (await fixture.Repository.GetCompiledRequestAsync(result.Request.Id))!.ContentHash);
        Assert.Equal(binding.Id,
            Assert.Single(await fixture.Repository.ListReferenceBindingsAsync(result.Request.Id)).Id);
    }

    [Fact]
    public async Task IdentityCompilation_SelectsAnyExactCapableModelAndPersistsRequestStrategy()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.CreateIdentityCapabilityAsync("identity-a", "model-a");
        var selected = await fixture.CreateIdentityCapabilityAsync("identity-b", "model-b");
        var intent = fixture.Intent();
        intent.VisibleActorsJson = "[{\"description\":\"a woman\",\"clothing\":\"yellow dress\"}]";
        intent.ContentHash = ProductionContentHash.ForIntent(intent);
        await fixture.Repository.CreateIntentAsync(intent);
        var requestId = "identity-request-selected-model";
        var strategy = fixture.IdentityBinding(
            requestId, selected.Profile.Id, selected.Cell.Id, CharacterIdentityStrategyKind.ReferenceConditioning);
        var service = new ProductionMediaCompilationService(
            fixture.Repository, new ProductionMediaCompilerRegistry([new SdxlProductionMediaCompiler()]),
            fixture.LoraRepository, fixture.ModelRepository);

        var result = await service.CompileIdentityAndPersistAsync(
            requestId, intent.Id, selected.Profile.Id, selected.Cell.Id,
            "{\"negativePrompt\":\"deformed\",\"width\":1024,\"height\":1024,\"steps\":30,\"guidance\":5,\"sampler\":\"dpmpp_2m_sde\",\"scheduler\":\"karras\",\"seed\":42}",
            [], [strategy], DateTime.UtcNow);

        Assert.NotEqual(first.Profile.Id, result.Request.CapabilityProfileId);
        Assert.Equal("model-b", result.Request.ModelId);
        Assert.Contains("ReferenceConditioning", result.Request.IdentityStrategySnapshotJson, StringComparison.Ordinal);
        Assert.Equal(strategy.Id,
            Assert.Single(await fixture.LoraRepository.ListIdentityStrategyBindingsAsync(requestId)).Id);
    }

    [Fact]
    public async Task IdentityCompilation_RejectsUnqualifiedStrategyBeforeRequestPersistence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var capability = await fixture.CreateIdentityCapabilityAsync("identity-reference", "model-reference");
        var intent = fixture.Intent();
        intent.VisibleActorsJson = "[{\"description\":\"a woman\",\"clothing\":\"yellow dress\"}]";
        intent.ContentHash = ProductionContentHash.ForIntent(intent);
        await fixture.Repository.CreateIntentAsync(intent);
        var requestId = "identity-request-unqualified";
        var strategy = fixture.IdentityBinding(
            requestId, capability.Profile.Id, capability.Cell.Id, CharacterIdentityStrategyKind.Lora);
        var service = new ProductionMediaCompilationService(
            fixture.Repository, new ProductionMediaCompilerRegistry([new SdxlProductionMediaCompiler()]),
            fixture.LoraRepository, fixture.ModelRepository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompileIdentityAndPersistAsync(
                requestId, intent.Id, capability.Profile.Id, capability.Cell.Id,
                "{\"negativePrompt\":\"deformed\",\"width\":1024,\"height\":1024,\"steps\":30,\"guidance\":5,\"sampler\":\"dpmpp_2m_sde\",\"scheduler\":\"karras\",\"seed\":42}",
                [], [strategy], DateTime.UtcNow));

        Assert.Contains("not qualified", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await fixture.Repository.GetCompiledRequestAsync(requestId));
    }

    [Fact]
    public async Task IdentityRequestGraph_BindingConstraintFailureRollsBackRequest()
    {
        await using var fixture = await Fixture.CreateAsync();
        var capability = await fixture.CreateIdentityCapabilityAsync("identity-atomic", "model-atomic");
        var intent = fixture.Intent();
        intent.VisibleActorsJson = "[{\"description\":\"a woman\"}]";
        intent.ContentHash = ProductionContentHash.ForIntent(intent);
        await fixture.Repository.CreateIntentAsync(intent);
        var request = fixture.Request(intent, capability.Profile, capability.Cell);
        request.Id = "identity-request-rollback";
        request.IdentityStrategySnapshotJson = "[]";
        request.ContentHash = ProductionContentHash.ForCompiledRequest(request, []);
        var first = fixture.IdentityBinding(
            request.Id, capability.Profile.Id, capability.Cell.Id, CharacterIdentityStrategyKind.ReferenceConditioning);
        var duplicateActor = fixture.IdentityBinding(
            request.Id, capability.Profile.Id, capability.Cell.Id, CharacterIdentityStrategyKind.ReferenceConditioning);

        await Assert.ThrowsAsync<SqliteException>(() => fixture.Repository.CreateIdentityCompiledRequestAsync(
            request, [], [first, duplicateActor]));

        Assert.Null(await fixture.Repository.GetCompiledRequestAsync(request.Id));
        Assert.Empty(await fixture.LoraRepository.ListIdentityStrategyBindingsAsync(request.Id));
    }

    [Fact]
    public async Task IdentityCompilation_RejectsStrategyMissingFromLinkedModelBeforePersistence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var capability = await fixture.CreateIdentityCapabilityAsync("identity-model-config", "model-config");
        var registeredModel = (await fixture.ModelRepository.GetByIdAsync(capability.Profile.RegisteredModelId))!;
        registeredModel.SupportedIdentityStrategiesJson = "[]";
        await fixture.ModelRepository.SaveAsync(registeredModel);
        var intent = fixture.Intent();
        intent.VisibleActorsJson = "[{\"description\":\"a woman\"}]";
        intent.ContentHash = ProductionContentHash.ForIntent(intent);
        await fixture.Repository.CreateIntentAsync(intent);
        var requestId = "identity-request-model-config";
        var strategy = fixture.IdentityBinding(
            requestId, capability.Profile.Id, capability.Cell.Id, CharacterIdentityStrategyKind.ReferenceConditioning);
        var service = new ProductionMediaCompilationService(
            fixture.Repository, new ProductionMediaCompilerRegistry([new SdxlProductionMediaCompiler()]),
            fixture.LoraRepository, fixture.ModelRepository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompileIdentityAndPersistAsync(
                requestId, intent.Id, capability.Profile.Id, capability.Cell.Id,
                "{\"negativePrompt\":\"deformed\",\"width\":1024,\"height\":1024,\"steps\":30,\"guidance\":5,\"sampler\":\"dpmpp_2m_sde\",\"scheduler\":\"karras\",\"seed\":42}",
                [], [strategy], DateTime.UtcNow));

        Assert.Contains("does not declare strategy", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await fixture.Repository.GetCompiledRequestAsync(requestId));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _dbPath;
        public ProductionMediaRepository Repository { get; }
        public ICharacterLoraRepository LoraRepository { get; }
        public TestRegisteredModelRepository ModelRepository { get; } = new();
        public SceneAssetRepository Assets { get; }
        public SceneAsset Asset { get; private set; } = null!;

        private Fixture(string dbPath, ProductionMediaRepository repository, SceneAssetRepository assets)
        {
            _dbPath = dbPath;
            Repository = repository;
            var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
            LoraRepository = new CharacterLoraRepository(options);
            Assets = assets;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"production-media-{Guid.NewGuid():N}.db");
            var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
            var assets = new SceneAssetRepository(options);
            await assets.GetAsync("__schema_probe__");
            var fixture = new Fixture(dbPath, new ProductionMediaRepository(options), assets);
            await fixture.CreatePrerequisitesAsync(options);
            return fixture;
        }

        private async Task CreatePrerequisitesAsync(IOptions<PersistenceOptions> options)
        {
            var appearance = new CharacterAppearanceVersionRepository(options);
            await appearance.GetBodyProfileAsync("__schema_probe__");
            await using var connection = new SqliteConnection(options.Value.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS SceneImageProductionGroups (Id TEXT PRIMARY KEY); INSERT INTO SceneImageProductionGroups (Id) VALUES ('group-1');";
            await command.ExecuteNonQueryAsync();

            Asset = new SceneAsset
            {
                Id = "asset-1", Name = "Output", Kind = SceneAssetKind.Uploaded,
                Status = SceneAssetStatus.Complete, Type = SceneAssetType.CharacterBody,
                FileRelativePath = "assets/output.png", MediaType = "image/png",
                Width = 1024, Height = 1024, ByteLength = 100, Sha256 = Sha256,
                CompletedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow
            };
            await Assets.UpsertAsync(Asset);
            Asset = await Assets.ApproveForProductionAsync(
                Asset.Id, "{\"source\":\"test\"}", SceneAssetConsentState.Confirmed,
                SceneAssetLicenseState.Confirmed, "test-owned", SceneAssetApprovedUseScope.ProductionSource,
                "test-policy", "{}");
        }

        public ProductionIntentSnapshot Intent()
        {
            var intent = new ProductionIntentSnapshot
            {
                Id = Guid.NewGuid().ToString("N"), ContextKind = ProductionContextKind.SceneMoment,
                ContextId = "session-1", ContextSnapshotJson = "{}",
                ProductionGroupId = "group-1", SessionId = "session-1",
                CatalogueId = "catalogue-1", BeatId = "beat-1", BeatProductionPlanId = "plan-1",
                BeatProductionPlanVersion = 1, MomentSetId = "set-1", MomentSetVersion = 1,
                MomentId = "moment-1", MomentEnrichmentId = "enrichment-1", MomentEnrichmentRevision = 1,
                Pov = "Dean", Operation = MediaOperation.Generate, VisibleActorsJson = "[]",
                CompositionIntentJson = "{}", CameraIntentJson = "{}", StyleIntentJson = "{}",
                PreservationConstraintsJson = "{}", ChangeIntentJson = "{}", ContentPolicyJson = "{}",
                CreatedUtc = DateTime.UtcNow
            };
            intent.ContentHash = ProductionContentHash.ForIntent(intent);
            return intent;
        }

        public async Task<(MediaCapabilityProfile Profile, MediaCapabilityCell Cell)> CreateCapabilityAsync()
        {
            var profile = new MediaCapabilityProfile
            {
                Id = Guid.NewGuid().ToString("N"), ProviderKey = "runpod", ModelId = "model-1",
                ModelVersion = "v1", Operation = MediaOperation.Generate, CompilerId = "sdxl",
                CompilerVersion = "1.0.0", WorkflowRevision = "workflow-1", NodeRevision = "nodes-1",
                ArtifactManifestJson = "{}", SettingsSchemaJson = "{}", ReferenceLayoutJson = "[]",
                ControlLayoutJson = "{}", ContentPolicyKey = "test-policy",
                Status = MediaCapabilityProfileStatus.Qualified, Enabled = true,
                EvidenceRunId = "proof-1", CreatedUtc = DateTime.UtcNow
            };
            await Repository.CreateCapabilityProfileAsync(profile);
            var cell = new MediaCapabilityCell
            {
                Id = Guid.NewGuid().ToString("N"), CapabilityProfileId = profile.Id, ActorCount = 1,
                FaceAngleKey = "front", CropKey = "close", PoseClassKey = "portrait",
                CompositionClassKey = "single", ReferenceControlTupleJson = "{}",
                Status = MediaCapabilityCellStatus.Qualified, EvidenceRunId = "proof-1", CreatedUtc = DateTime.UtcNow
            };
            await Repository.AddCapabilityCellAsync(cell);
            return (profile, cell);
        }

        public OrderedMediaReferenceBinding Binding(string requestId, SceneAsset asset) => new()
        {
            Id = Guid.NewGuid().ToString("N"), CompiledRequestId = requestId, Ordinal = 0,
            SemanticRole = "composition source", SceneAssetId = asset.Id,
            SceneAssetVersion = asset.ProductionVersion!.Value, SceneAssetSha256 = asset.Sha256,
            BindingSnapshotJson = "{}", CreatedUtc = DateTime.UtcNow
        };

        public async Task<(MediaCapabilityProfile Profile, MediaCapabilityCell Cell)> CreateIdentityCapabilityAsync(
            string id, string modelId)
        {
            var registeredModel = new RegisteredModel
            {
                Id = $"{id}-registered", ProviderId = "provider-1", ModelIdentifier = modelId,
                DisplayName = modelId, ModelKind = ModelKind.Image, IsEnabled = true,
                SupportedIdentityStrategiesJson = "[\"ReferenceConditioning\"]"
            };
            await ModelRepository.SaveAsync(registeredModel);
            var profile = new MediaCapabilityProfile
            {
                Id = $"{id}-profile", RegisteredModelId = registeredModel.Id,
                ProviderKey = "runpod", ModelId = modelId, ModelVersion = "v1",
                Operation = MediaOperation.Generate, CompilerId = "sdxl-photographic", CompilerVersion = "1",
                WorkflowRevision = "workflow-identity", NodeRevision = "nodes-identity",
                ArtifactManifestJson = "{}", SettingsSchemaJson = "{}", ReferenceLayoutJson = "{}",
                ControlLayoutJson = "{}", SupportedIdentityStrategiesJson = "[\"ReferenceConditioning\"]",
                ContentPolicyKey = "test-policy", Status = MediaCapabilityProfileStatus.Qualified, Enabled = true,
                EvidenceRunId = $"{id}-proof", CreatedUtc = DateTime.UtcNow
            };
            await Repository.CreateCapabilityProfileAsync(profile);
            var cell = new MediaCapabilityCell
            {
                Id = $"{id}-cell", CapabilityProfileId = profile.Id, ActorCount = 1,
                FaceAngleKey = "front", CropKey = "medium", PoseClassKey = "portrait",
                CompositionClassKey = "single", ReferenceControlTupleJson = $"{{\"strategyCell\":\"{id}\"}}",
                IdentityStrategyKind = CharacterIdentityStrategyKind.ReferenceConditioning,
                Status = MediaCapabilityCellStatus.Qualified, EvidenceRunId = $"{id}-proof", CreatedUtc = DateTime.UtcNow
            };
            await Repository.AddCapabilityCellAsync(cell);
            return (profile, cell);
        }

        public IdentityStrategyBinding IdentityBinding(
            string requestId,
            string profileId,
            string cellId,
            CharacterIdentityStrategyKind strategy) => new()
        {
            Id = Guid.NewGuid().ToString("N"), CompiledRequestId = requestId, ActorKey = "actor-1",
            StrategyKind = strategy, CapabilityProfileId = profileId, CapabilityCellId = cellId,
            ReferenceBindingsJson = strategy is CharacterIdentityStrategyKind.ReferenceConditioning or CharacterIdentityStrategyKind.Combined ? "[]" : null,
            LoraArtifactId = strategy is CharacterIdentityStrategyKind.Lora or CharacterIdentityStrategyKind.Combined ? "artifact-1" : null,
            LoraArtifactSha256 = strategy is CharacterIdentityStrategyKind.Lora or CharacterIdentityStrategyKind.Combined ? Sha256 : null,
            LoraStrength = strategy is CharacterIdentityStrategyKind.Lora or CharacterIdentityStrategyKind.Combined ? 0.8m : null,
            BindingSnapshotJson = "{}", CreatedUtc = DateTime.UtcNow
        };

        public CompiledMediaRequest Request(
            ProductionIntentSnapshot intent, MediaCapabilityProfile profile, MediaCapabilityCell cell) => new()
        {
            Id = "request-1", IntentSnapshotId = intent.Id, CapabilityProfileId = profile.Id,
            CapabilityCellId = cell.Id, CompilerId = profile.CompilerId, CompilerVersion = profile.CompilerVersion,
            RequestSchemaVersion = "1", ProviderKey = profile.ProviderKey, ModelId = profile.ModelId,
            ModelVersion = profile.ModelVersion, WorkflowRevision = profile.WorkflowRevision,
            CanonicalProviderRequestJson = "{\"prompt\":\"portrait\"}", ValidationResultJson = "{\"ready\":true}",
            CreatedUtc = DateTime.UtcNow
        };

        public async Task<(ProductionIntentSnapshot Intent, CompiledMediaRequest Request, SceneAsset Asset)> CreateCompiledGraphAsync()
        {
            var capability = await CreateCapabilityAsync();
            var intent = Intent();
            await Repository.CreateIntentAsync(intent);
            var request = Request(intent, capability.Profile, capability.Cell);
            var binding = Binding(request.Id, Asset);
            request.ContentHash = ProductionContentHash.ForCompiledRequest(request, [binding]);
            await Repository.CreateCompiledRequestAsync(request, [binding]);
            return (intent, request, Asset);
        }

        public async Task<(ProductionWorkload Workload, ProductionWorkloadItem Item, (ProductionIntentSnapshot Intent, CompiledMediaRequest Request, SceneAsset Asset) Compiled)> CreateWorkloadGraphAsync()
        {
            var compiled = await CreateCompiledGraphAsync();
            var workload = new ProductionWorkload
            {
                Id = "workload-1", ContextKind = ProductionContextKind.SceneMoment,
                ContextId = "session-1", ContextSnapshotJson = "{}", SessionId = "session-1", Revision = 1,
                Status = ProductionWorkloadStatus.Draft, ConcurrencyVersion = 1,
                Goal = "render moment", ContentPolicyKey = "test-policy",
                SourceVersionSnapshotJson = "{}", ReadinessSnapshotJson = "{}",
                EndpointReadinessJson = "{}", CostEstimateJson = "{}",
                ItemCount = 1, OutputCount = 1, CompatibilityGroupCount = 1, CreatedUtc = DateTime.UtcNow
            };
            var item = new ProductionWorkloadItem
            {
                Id = "item-1", WorkloadId = workload.Id, Ordinal = 0,
                IntentSnapshotId = compiled.Intent.Id, CompiledRequestId = compiled.Request.Id,
                CompatibilityKey = "group-1", VariationCount = 1,
                Status = ProductionWorkloadItemStatus.Draft, ConcurrencyVersion = 1,
                RetryPolicySnapshotJson = "{\"maxAttempts\":1}",
                EndpointSnapshotJson = "{\"endpointId\":\"provider-1\"}",
                DispatchPolicySnapshotJson = "{\"adapterKey\":\"test\"}",
                CostBasisSnapshotJson = "{\"currency\":\"USD\",\"unitCostPerOutput\":0.1}",
                CreatedUtc = DateTime.UtcNow
            };
            await Repository.CreateWorkloadAsync(workload, [item]);
            return (workload, item, compiled);
        }

        public ProductionAttempt Attempt(ProductionWorkloadItem item, CompiledMediaRequest request) => new()
        {
            Id = "attempt-1", WorkloadItemId = item.Id, AttemptNumber = 1,
            Kind = ProductionAttemptKind.Initial, Status = ProductionAttemptStatus.Pending,
            ConcurrencyVersion = 1, CompiledRequestId = request.Id, CompiledRequestHash = request.ContentHash,
            RequestSnapshotJson = request.CanonicalProviderRequestJson, ReferenceSnapshotJson = "[]",
            ModelWorkflowSnapshotJson = "{}", SettingsSnapshotJson = "{}", Seed = 42,
            CreatedUtc = DateTime.UtcNow
        };

        public sealed class TestRegisteredModelRepository : IRegisteredModelRepository
        {
            private readonly Dictionary<string, RegisteredModel> _models = new(StringComparer.Ordinal);

            public Task<RegisteredModel> SaveAsync(RegisteredModel model, CancellationToken cancellationToken = default)
            {
                _models[model.Id] = model;
                return Task.FromResult(model);
            }

            public Task<RegisteredModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
                Task.FromResult(_models.GetValueOrDefault(id));

            public Task<List<RegisteredModel>> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default) =>
                Task.FromResult(_models.Values.Where(model => model.ProviderId == providerId).ToList());

            public Task<List<RegisteredModel>> GetAllEnabledAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(_models.Values.Where(model => model.IsEnabled).ToList());

            public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
                Task.FromResult(_models.Remove(id));

            public Task<bool> ExistsByProviderAndIdentifierAsync(string providerId, string modelIdentifier, CancellationToken cancellationToken = default) =>
                Task.FromResult(_models.Values.Any(model => model.ProviderId == providerId && model.ModelIdentifier == modelIdentifier));
        }

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(_dbPath + suffix); } catch { }
            }
            return ValueTask.CompletedTask;
        }
    }
}