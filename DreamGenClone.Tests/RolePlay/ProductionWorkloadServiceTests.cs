using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ProductionWorkloadServiceTests
{
    [Fact]
    public async Task CharacterAssetBatch_CreatesOneExactGraphPerCandidate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var compilation = new ProductionMediaCompilationService(
            fixture.Repository, new ProductionMediaCompilerRegistry([new FakeProductionCompiler()]),
            fixture.LoraRepository, null!);
        await fixture.SeedIdentityPackAsync("pack-1", "character-1", CharacterImageIdentityPackStatus.Approved);
        var service = new CharacterAssetGenerationService(
            fixture.Repository, compilation, fixture.Service, fixture.IdentityRepository);
        var endpoint = new ProductionProviderEndpoint(
            "provider-key", "endpoint-1", "https://provider.invalid/v2/endpoint-1",
            "/run", "/status/{jobId}", "/cancel/{jobId}", 30,
            FakeDispatchAdapter.Key, "{\"ready\":true}");
        var policy = new ProductionDispatchPolicy(
            FakeDispatchAdapter.Key, false, 1, "worker:v1", "artifacts:v1", "inline", 600);
        var candidates = new[]
        {
            Candidate("seed", CharacterAssetCandidateKind.IdentitySeed, "front-close", SceneAssetType.CharacterFace),
            Candidate("coverage", CharacterAssetCandidateKind.Coverage, "three-quarter-body", SceneAssetType.CharacterBody)
        };

        var results = await service.CreateBatchAsync(new CharacterAssetGenerationBatch(
            "dataset-1", "character-1", "pack-1", "profile-1", "cell-1", "policy-1",
            "{\"identityVersion\":1}", "{\"maxAttempts\":2}", endpoint, policy,
            new ProductionCostBasis("USD", 0.1m), candidates, DateTime.UtcNow));

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Equal(ProductionContextKind.CharacterAsset, result.Workload.ContextKind));
        Assert.All(results, result => Assert.Empty(result.Workload.SessionId));
        Assert.Equal("IdentitySeed", JsonDocument.Parse(results[0].Workload.ContextSnapshotJson).RootElement.GetProperty("candidateKind").GetString());
        Assert.Equal("Coverage", JsonDocument.Parse(results[1].Workload.ContextSnapshotJson).RootElement.GetProperty("candidateKind").GetString());

        fixture.Adapter.ImmediateSuccess = true;
        await fixture.Service.SubmitAsync(results[0].Workload.Id);
        var item = Assert.Single(await fixture.Repository.ListWorkloadItemsAsync(results[0].Workload.Id));
        var attempt = Assert.Single(await fixture.Repository.ListAttemptsAsync(item.Id));
        var asset = await fixture.Assets.GetAsync(attempt.Id);
        Assert.NotNull(asset);
        Assert.Equal(SceneAssetStatus.Complete, asset.Status);
        Assert.Equal(SceneAssetProductionApprovalStatus.Draft, asset.ProductionApprovalStatus);
        Assert.Equal(attempt.OutputSha256, asset.Sha256);
        Assert.Contains(attempt.Id, asset.SourceProvenanceJson, StringComparison.Ordinal);

        static CharacterAssetCandidateDraft Candidate(
            string key, CharacterAssetCandidateKind kind, string coverage, SceneAssetType assetType) => new(
                $"workload-{key}", $"intent-{key}", $"request-{key}", $"Asset {key}", assetType,
                kind, coverage, "Dean", "[]", "{}", "{}", "{}", "{}", "{}",
                "{\"width\":1024,\"height\":1024,\"seed\":42}", [], []);
    }

    [Theory]
    [InlineData(CharacterImageIdentityPackStatus.Draft, "character-1")]
    [InlineData(CharacterImageIdentityPackStatus.Superseded, "character-1")]
    [InlineData(CharacterImageIdentityPackStatus.Approved, "character-2")]
    public async Task CharacterAssetBatch_RejectsUnusableIdentityPackBeforePersistence(
        CharacterImageIdentityPackStatus status,
        string packCharacterId)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedIdentityPackAsync("pack-1", packCharacterId, status);
        var compilation = new ProductionMediaCompilationService(
            fixture.Repository, new ProductionMediaCompilerRegistry([new FakeProductionCompiler()]),
            fixture.LoraRepository, null!);
        var service = new CharacterAssetGenerationService(
            fixture.Repository, compilation, fixture.Service, fixture.IdentityRepository);
        var candidate = new CharacterAssetCandidateDraft(
            "workload-invalid-pack", "intent-invalid-pack", "request-invalid-pack", "Invalid pack asset",
            SceneAssetType.CharacterFace, CharacterAssetCandidateKind.IdentitySeed, "front-close", "Dean",
            "[]", "{}", "{}", "{}", "{}", "{}", "{\"width\":1024,\"height\":1024,\"seed\":42}", [], []);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateBatchAsync(
            new CharacterAssetGenerationBatch(
                "dataset-1", "character-1", "pack-1", "profile-1", "cell-1", "policy-1",
                "{\"identityVersion\":1}", "{\"maxAttempts\":2}",
                new ProductionProviderEndpoint(
                    "provider-key", "endpoint-1", "https://provider.invalid/v2/endpoint-1",
                    "/run", "/status/{jobId}", "/cancel/{jobId}", 30,
                    FakeDispatchAdapter.Key, "{\"ready\":true}"),
                new ProductionDispatchPolicy(
                    FakeDispatchAdapter.Key, false, 1, "worker:v1", "artifacts:v1", "inline", 600),
                new ProductionCostBasis("USD", 0.1m), [candidate], DateTime.UtcNow)));

        Assert.Contains("Identity pack", error.Message, StringComparison.Ordinal);
        Assert.Null(await fixture.Repository.GetIntentAsync(candidate.IntentId));
    }

    [Fact]
    public async Task CharacterAssetBatch_PersistsExplicitIdentityStrategyForCoverageCandidate()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedIdentityPackAsync("pack-1", "character-1", CharacterImageIdentityPackStatus.Approved);
        var compilation = new ProductionMediaCompilationService(
            fixture.Repository, new ProductionMediaCompilerRegistry([new FakeProductionCompiler()]),
            fixture.LoraRepository, new FakeRegisteredModelRepository());
        var service = new CharacterAssetGenerationService(
            fixture.Repository, compilation, fixture.Service, fixture.IdentityRepository);
        var requestId = "request-identity-coverage";
        var identityBinding = new IdentityStrategyBinding
        {
            Id = "binding-identity-coverage",
            CompiledRequestId = requestId,
            ActorKey = "Dean",
            StrategyKind = CharacterIdentityStrategyKind.ReferenceConditioning,
            CapabilityProfileId = "profile-1",
            CapabilityCellId = "cell-1",
            ReferenceBindingsJson = "[]",
            BindingSnapshotJson = "{\"actorKey\":\"Dean\",\"strategyKind\":\"ReferenceConditioning\"}",
            CreatedUtc = DateTime.UtcNow
        };
        var candidate = new CharacterAssetCandidateDraft(
            "workload-identity-coverage", "intent-identity-coverage", requestId, "Identity coverage",
            SceneAssetType.CharacterFace, CharacterAssetCandidateKind.Coverage, "front-close", "Dean",
            "[]", "{}", "{}", "{}", "{}", "{}", "{\"width\":1024,\"height\":1024,\"seed\":42}",
            [], [identityBinding]);

        await service.CreateBatchAsync(new CharacterAssetGenerationBatch(
            "dataset-1", "character-1", "pack-1", "profile-1", "cell-1", "policy-1",
            "{\"identityVersion\":1}", "{\"maxAttempts\":2}",
            new ProductionProviderEndpoint(
                "provider-key", "endpoint-1", "https://provider.invalid/v2/endpoint-1",
                "/run", "/status/{jobId}", "/cancel/{jobId}", 30,
                FakeDispatchAdapter.Key, "{\"ready\":true}"),
            new ProductionDispatchPolicy(
                FakeDispatchAdapter.Key, false, 1, "worker:v1", "artifacts:v1", "inline", 600),
            new ProductionCostBasis("USD", 0.1m), [candidate], DateTime.UtcNow));

        var persisted = Assert.Single(await fixture.LoraRepository.ListIdentityStrategyBindingsAsync(requestId));
        Assert.Equal(CharacterIdentityStrategyKind.ReferenceConditioning, persisted.StrategyKind);
        Assert.Equal("Dean", persisted.ActorKey);
    }

    [Fact]
    public async Task CreateDraft_PersistsExplicitReadinessGroupingOutputCountAndCost()
    {
        await using var fixture = await Fixture.CreateAsync();
        var draft = await fixture.DraftAsync(itemCount: 2, variations: 2, unitCost: 0.25m);

        var readiness = await fixture.Service.CreateDraftAsync(draft);

        Assert.Equal(ProductionWorkloadStatus.Ready, readiness.Workload.Status);
        Assert.Equal(2, readiness.Workload.ItemCount);
        Assert.Equal(4, readiness.Workload.OutputCount);
        Assert.Equal(1, readiness.Workload.CompatibilityGroupCount);
        Assert.Contains("beatProductionPlanVersion", readiness.Workload.SourceVersionSnapshotJson);
        Assert.Contains("endpoint-1", readiness.Workload.EndpointReadinessJson);
        using var cost = JsonDocument.Parse(readiness.Workload.CostEstimateJson);
        Assert.Equal(1.0m, cost.RootElement.GetProperty("estimatedCost").GetDecimal());
        Assert.Equal("USD", cost.RootElement.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task LoadSession_ReturnsPersistedWorkloadItemAndAttemptTree()
    {
        await using var fixture = await Fixture.CreateAsync();
        var readiness = await fixture.Service.CreateDraftAsync(await fixture.DraftAsync());
        await fixture.Service.SubmitAsync(readiness.Workload.Id);
        var persistedItem = Assert.Single(await fixture.Repository.ListWorkloadItemsAsync(readiness.Workload.Id));
        var persistedAttempt = Assert.Single(await fixture.Repository.ListAttemptsAsync(persistedItem.Id));

        var snapshots = await fixture.Service.LoadSessionAsync("session-1");

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(readiness.Workload.Id, snapshot.Workload.Id);
        var item = Assert.Single(snapshot.Items);
        Assert.Equal(persistedItem.Id, item.Item.Id);
        Assert.Equal(persistedItem.IntentSnapshotId, item.Intent.Id);
        Assert.Equal(persistedItem.CompiledRequestId, item.Request.Id);
        Assert.Empty(item.ReferenceBindings);
        Assert.Equal(persistedAttempt.Id, Assert.Single(item.Attempts).Id);
        Assert.Empty(item.ReviewDecisions);
        Assert.Empty(await fixture.Service.LoadSessionAsync("other-session"));
    }

    [Fact]
    public async Task Restart_ReconcilesPersistedProviderIdWithoutDuplicateSubmission()
    {
        await using var fixture = await Fixture.CreateAsync();
        var readiness = await fixture.Service.CreateDraftAsync(await fixture.DraftAsync());
        await fixture.Service.SubmitAsync(readiness.Workload.Id);
        var item = Assert.Single(await fixture.Repository.ListWorkloadItemsAsync(readiness.Workload.Id));
        var attempt = Assert.Single(await fixture.Repository.ListAttemptsAsync(item.Id));
        Assert.Equal("job-1", attempt.ProviderRequestId);
        Assert.Equal(1, fixture.Adapter.SubmissionCount);

        fixture.Adapter.PollResults["job-1"] = Fixture.SuccessResult();
        var restarted = fixture.CreateServices();
        await restarted.Reconciliation.ReconcileWorkloadAsync(readiness.Workload.Id);

        Assert.Equal(1, fixture.Adapter.SubmissionCount);
        attempt = (await fixture.Repository.GetAttemptAsync(attempt.Id))!;
        Assert.Equal(ProductionAttemptStatus.Succeeded, attempt.Status);
        Assert.NotNull(attempt.OutputFileRelativePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.Service.SubmitAsync(readiness.Workload.Id));
        Assert.Equal(1, fixture.Adapter.SubmissionCount);
    }

    [Fact]
    public async Task NativeVariations_CreateIndependentAttemptsAndAccountEachOutput()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Adapter.ImmediateSuccess = true;
        var readiness = await fixture.Service.CreateDraftAsync(
            await fixture.DraftAsync(variations: 3, unitCost: 0.20m, nativeVariations: true));

        await fixture.Service.SubmitAsync(readiness.Workload.Id);

        var item = Assert.Single(await fixture.Repository.ListWorkloadItemsAsync(readiness.Workload.Id));
        var attempts = await fixture.Repository.ListAttemptsAsync(item.Id);
        Assert.Equal(3, attempts.Count);
        Assert.All(attempts, attempt => Assert.Equal(ProductionAttemptStatus.Succeeded, attempt.Status));
        Assert.All(attempts, attempt => Assert.Contains("\"amount\":0.20", attempt.CostSnapshotJson));
        Assert.Equal(3, fixture.Storage.Files.Count);
        Assert.Equal(ProductionWorkloadItemStatus.Reviewable,
            (await fixture.Repository.GetWorkloadItemAsync(item.Id))!.Status);
        Assert.Equal(ProductionWorkloadStatus.Complete,
            (await fixture.Repository.GetWorkloadAsync(readiness.Workload.Id))!.Status);
    }

    [Fact]
    public async Task SceneOutput_RegistersDraftAssetAndApprovesExactDerivative()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Adapter.ImmediateSuccess = true;
        var readiness = await fixture.Service.CreateDraftAsync(await fixture.DraftAsync());
        await fixture.Service.SubmitAsync(readiness.Workload.Id);
        var item = Assert.Single(await fixture.Repository.ListWorkloadItemsAsync(readiness.Workload.Id));
        var attempt = Assert.Single(await fixture.Repository.ListAttemptsAsync(item.Id));
        var capturedAsset = await fixture.Assets.GetAsync(attempt.Id);

        Assert.NotNull(capturedAsset);
        Assert.Equal(SceneAssetType.ProductionFrame, capturedAsset.Type);
        Assert.Equal(SceneAssetProductionApprovalStatus.Draft, capturedAsset.ProductionApprovalStatus);
        Assert.Equal(attempt.OutputSha256, capturedAsset.Sha256);

        var approvedUtc = DateTime.UtcNow;
        var derivative = await fixture.Service.ApproveAsync(new ProductionApprovalCommand(
            item.Id, attempt.Id, "selected_output", "Exact output accepted", "reviewer-1",
            $"{{\"attemptId\":\"{attempt.Id}\",\"sha256\":\"{attempt.OutputSha256}\"}}",
            SceneAssetConsentState.NotApplicable, SceneAssetLicenseState.Confirmed,
            "provider-output-license", SceneAssetApprovedUseScope.ProductionSource,
            "policy-1", "{\"modelCompatible\":true}", "scene-production", approvedUtc));

        var approvedAsset = await fixture.Assets.GetAsync(attempt.Id);
        var reviews = await fixture.Repository.ListReviewDecisionsAsync(item.Id);
        Assert.Equal(SceneAssetProductionApprovalStatus.Approved, approvedAsset!.ProductionApprovalStatus);
        Assert.Equal(ProductionReviewDecisionValue.Approved, Assert.Single(reviews).Decision);
        Assert.Equal(attempt.Id, derivative.SceneAssetId);
        Assert.Equal(derivative.Id, (await fixture.Repository.GetDerivativeAsync(derivative.Id))!.Id);
        Assert.Equal(ProductionWorkloadItemStatus.Approved,
            (await fixture.Repository.GetWorkloadItemAsync(item.Id))!.Status);
    }

    [Fact]
    public async Task StudioPrepare_CreatesNewSemanticIntentAndReadyWorkloadRevision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = await fixture.Service.CreateDraftAsync(await fixture.DraftAsync());
        var sourceItem = Assert.Single(source.Items);
        var sourceIntent = (await fixture.Repository.GetIntentAsync(sourceItem.IntentSnapshotId))!;
        var compilation = new ProductionMediaCompilationService(
            fixture.Repository, new ProductionMediaCompilerRegistry([new FakeProductionCompiler()]),
            fixture.LoraRepository, null!);
        var studio = new ProductionStudioService(fixture.Repository, compilation, fixture.Service, fixture.Assets);
        var createdUtc = DateTime.UtcNow;

        var prepared = await studio.PrepareAsync(new ProductionPrepareCommand(
            sourceItem.Id, "[{\"description\":\"one adult\"}]",
            "{\"composition\":\"left-weighted\"}", "{\"framing\":\"medium\"}",
            "{\"style\":\"photo\"}", "{}", "{}", "{}", "profile-1", "cell-1",
            "{\"width\":1024,\"height\":1024,\"seed\":808}", [], 1,
            "recompose selected moment", "{\"maxAttempts\":2}",
            new ProductionProviderEndpoint(
                "provider-key", "endpoint-1", "https://provider.invalid/v2/endpoint-1",
                "/run", "/status/{jobId}", "/cancel/{jobId}", 30,
                FakeDispatchAdapter.Key, "{\"ready\":true}"),
            new ProductionDispatchPolicy(
                FakeDispatchAdapter.Key, false, 1, "worker:v1", "artifacts:v1", "inline", 600),
            new ProductionCostBasis("USD", 0.25m), createdUtc));

        var preparedItem = Assert.Single(prepared.Items);
        var preparedIntent = (await fixture.Repository.GetIntentAsync(preparedItem.IntentSnapshotId))!;
        Assert.Equal(ProductionWorkloadStatus.Ready, prepared.Workload.Status);
        Assert.Equal(2, prepared.Workload.Revision);
        Assert.NotEqual(sourceIntent.Id, preparedIntent.Id);
        Assert.Equal(sourceIntent.MomentId, preparedIntent.MomentId);
        Assert.Equal(sourceIntent.MomentEnrichmentRevision, preparedIntent.MomentEnrichmentRevision);
        Assert.Contains("left-weighted", preparedIntent.CompositionIntentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("left-weighted", sourceIntent.CompositionIntentJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialGroup_LeavesRunningItemAndMarksWorkloadPartiallyComplete()
    {
        await using var fixture = await Fixture.CreateAsync();
        var readiness = await fixture.Service.CreateDraftAsync(await fixture.DraftAsync(itemCount: 2));
        await fixture.Service.SubmitAsync(readiness.Workload.Id);
        fixture.Adapter.PollResults["job-1"] = Fixture.SuccessResult();
        fixture.Adapter.PollResults["job-2"] = Fixture.RunningResult();

        await fixture.Reconciliation.ReconcileWorkloadAsync(readiness.Workload.Id);

        var items = await fixture.Repository.ListWorkloadItemsAsync(readiness.Workload.Id);
        Assert.Contains(items, item => item.Status == ProductionWorkloadItemStatus.Reviewable);
        Assert.Contains(items, item => item.Status == ProductionWorkloadItemStatus.Running);
        Assert.Equal(ProductionWorkloadStatus.PartiallyComplete,
            (await fixture.Repository.GetWorkloadAsync(readiness.Workload.Id))!.Status);
    }

    [Fact]
    public async Task LateResult_IsOwnedButCannotReplaceNewRetryAttempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var readiness = await fixture.Service.CreateDraftAsync(await fixture.DraftAsync());
        await fixture.Service.SubmitAsync(readiness.Workload.Id);
        var item = Assert.Single(await fixture.Repository.ListWorkloadItemsAsync(readiness.Workload.Id));
        var first = Assert.Single(await fixture.Repository.ListAttemptsAsync(item.Id));
        fixture.Adapter.PollResults["job-1"] = Fixture.ExpiredResult();
        await fixture.Reconciliation.ReconcileAttemptAsync(first.Id);
        var retry = await fixture.Service.RetryAsync(item.Id, first.Id, DateTime.UtcNow);

        fixture.Adapter.PollResults["job-1"] = Fixture.SuccessResult();
        await fixture.Reconciliation.ReconcileAttemptAsync(first.Id);

        first = (await fixture.Repository.GetAttemptAsync(first.Id))!;
        item = (await fixture.Repository.GetWorkloadItemAsync(item.Id))!;
        Assert.Equal(ProductionAttemptStatus.Indeterminate, first.Status);
        Assert.NotNull(first.OutputFileRelativePath);
        Assert.Equal(retry.Id, item.CurrentAttemptId);
        Assert.Contains("\"late\":true", first.OutputMetadataJson);
    }

    [Fact]
    public async Task PollTimeout_BecomesIndeterminateWithoutResubmission()
    {
        await using var fixture = await Fixture.CreateAsync();
        var readiness = await fixture.Service.CreateDraftAsync(await fixture.DraftAsync());
        await fixture.Service.SubmitAsync(readiness.Workload.Id);
        var item = Assert.Single(await fixture.Repository.ListWorkloadItemsAsync(readiness.Workload.Id));
        var attempt = Assert.Single(await fixture.Repository.ListAttemptsAsync(item.Id));
        fixture.Adapter.ThrowTimeout = true;

        await fixture.Reconciliation.ReconcileAttemptAsync(attempt.Id);

        attempt = (await fixture.Repository.GetAttemptAsync(attempt.Id))!;
        Assert.Equal(ProductionAttemptStatus.Indeterminate, attempt.Status);
        Assert.Equal("provider_poll_timeout", attempt.FailureCode);
        Assert.Equal(1, fixture.Adapter.SubmissionCount);
    }

    [Fact]
    public async Task Cancellation_CancelsProviderAttemptAndWorkload()
    {
        await using var fixture = await Fixture.CreateAsync();
        var readiness = await fixture.Service.CreateDraftAsync(await fixture.DraftAsync());
        await fixture.Service.SubmitAsync(readiness.Workload.Id);

        await fixture.Service.CancelAsync(readiness.Workload.Id);

        var item = Assert.Single(await fixture.Repository.ListWorkloadItemsAsync(readiness.Workload.Id));
        var attempt = Assert.Single(await fixture.Repository.ListAttemptsAsync(item.Id));
        Assert.Equal(1, fixture.Adapter.CancellationCount);
        Assert.Equal(ProductionAttemptStatus.Cancelled, attempt.Status);
        Assert.Equal(ProductionWorkloadItemStatus.Cancelled, item.Status);
        Assert.Equal(ProductionWorkloadStatus.Cancelled,
            (await fixture.Repository.GetWorkloadAsync(readiness.Workload.Id))!.Status);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _dbPath;
        private readonly MediaCapabilityProfile _profile;
        private readonly MediaCapabilityCell _cell;
        public ProductionMediaRepository Repository { get; }
        public ICharacterLoraRepository LoraRepository { get; }
        public ICharacterImageIdentityRepository IdentityRepository { get; }
        public ISceneAssetRepository Assets { get; }
        public FakeDispatchAdapter Adapter { get; } = new();
        public FakeStorage Storage { get; } = new();
        public IProductionWorkloadService Service { get; private set; } = null!;
        public IProductionReconciliationService Reconciliation { get; private set; } = null!;

        private Fixture(
            string dbPath,
            ProductionMediaRepository repository,
            ICharacterLoraRepository loraRepository,
            ICharacterImageIdentityRepository identityRepository,
            ISceneAssetRepository assets,
            MediaCapabilityProfile profile,
            MediaCapabilityCell cell)
        {
            _dbPath = dbPath;
            Repository = repository;
            LoraRepository = loraRepository;
            IdentityRepository = identityRepository;
            Assets = assets;
            _profile = profile;
            _cell = cell;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"production-workload-{Guid.NewGuid():N}.db");
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={dbPath};Pooling=False",
                SceneImageRoot = Path.Combine(Path.GetTempPath(), $"production-output-{Guid.NewGuid():N}")
            });
            await using (var connection = new SqliteConnection(options.Value.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE SceneImageProductionGroups (Id TEXT PRIMARY KEY); INSERT INTO SceneImageProductionGroups (Id) VALUES ('group-1');";
                await command.ExecuteNonQueryAsync();
            }
            var repository = new ProductionMediaRepository(options);
            var profile = new MediaCapabilityProfile
            {
                Id = "profile-1", ProviderKey = "provider-key", ModelId = "model-1", ModelVersion = "v1",
                Operation = MediaOperation.Generate, CompilerId = "compiler-1", CompilerVersion = "1",
                RegisteredModelId = FakeRegisteredModelRepository.ModelId,
                SupportedIdentityStrategiesJson = "[\"ReferenceConditioning\"]",
                WorkflowRevision = "workflow-1", NodeRevision = "nodes-1", ArtifactManifestJson = "{}",
                SettingsSchemaJson = "{}", ReferenceLayoutJson = "[]", ControlLayoutJson = "{}",
                ContentPolicyKey = "policy-1", Status = MediaCapabilityProfileStatus.Qualified, Enabled = true,
                EvidenceRunId = "evidence-1", CreatedUtc = DateTime.UtcNow
            };
            await repository.CreateCapabilityProfileAsync(profile);
            var cell = new MediaCapabilityCell
            {
                Id = "cell-1", CapabilityProfileId = profile.Id, ActorCount = 1,
                FaceAngleKey = "front", CropKey = "medium", PoseClassKey = "standing",
                CompositionClassKey = "single", ReferenceControlTupleJson = "{}",
                IdentityStrategyKind = CharacterIdentityStrategyKind.ReferenceConditioning,
                Status = MediaCapabilityCellStatus.Qualified, EvidenceRunId = "evidence-1",
                CreatedUtc = DateTime.UtcNow
            };
            await repository.AddCapabilityCellAsync(cell);
            var fixture = new Fixture(
                dbPath, repository, new CharacterLoraRepository(options),
                new CharacterImageIdentityRepository(options),
                new SceneAssetRepository(options), profile, cell);
            fixture.CreateServices();
            return fixture;
        }

        public async Task SeedIdentityPackAsync(
            string packId,
            string characterProfileId,
            CharacterImageIdentityPackStatus status)
        {
            await IdentityRepository.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                Id = packId,
                CharacterProfileId = characterProfileId,
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft,
                DescriptorSnapshotJson = "{\"description\":\"test identity\"}",
                CreatedUtc = DateTime.UtcNow
            });
            if (status == CharacterImageIdentityPackStatus.Draft) return;

            await using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE CharacterImageIdentityPacks SET Status = $status WHERE Id = $id;";
            command.Parameters.AddWithValue("$status", status.ToString());
            command.Parameters.AddWithValue("$id", packId);
            await command.ExecuteNonQueryAsync();
        }

        public (IProductionWorkloadService Service, IProductionReconciliationService Reconciliation) CreateServices()
        {
            var registry = new ProductionDispatchAdapterRegistry([Adapter]);
            Reconciliation = new ProductionReconciliationService(
                Repository, registry, Storage, Assets, new FakeHttpClientFactory());
            Service = new ProductionWorkloadService(Repository, registry, Reconciliation, Assets);
            return (Service, Reconciliation);
        }

        public async Task<ProductionWorkloadDraft> DraftAsync(
            int itemCount = 1,
            int variations = 1,
            decimal unitCost = 0.10m,
            bool nativeVariations = false)
        {
            var items = new List<ProductionWorkloadDraftItem>();
            for (var index = 0; index < itemCount; index++)
            {
                var intent = new ProductionIntentSnapshot
                {
                    Id = $"intent-{Guid.NewGuid():N}", ContextKind = ProductionContextKind.SceneMoment,
                    ContextId = "session-1", ContextSnapshotJson = "{}",
                    ProductionGroupId = "group-1", SessionId = "session-1",
                    CatalogueId = "catalogue-1", BeatId = "beat-1", BeatProductionPlanId = "plan-1",
                    BeatProductionPlanVersion = 2, MomentSetId = "set-1", MomentSetVersion = 3,
                    MomentId = $"moment-{index}", MomentEnrichmentId = $"enrichment-{index}",
                    MomentEnrichmentRevision = 4, Pov = "Dean", Operation = MediaOperation.Generate,
                    VisibleActorsJson = "[{\"description\":\"one adult\"}]",
                    CompositionIntentJson = "{\"composition\":\"centered\"}",
                    CameraIntentJson = "{\"framing\":\"medium\"}", StyleIntentJson = "{\"style\":\"photo\"}",
                    PreservationConstraintsJson = "{}", ChangeIntentJson = "{}", ContentPolicyJson = "{}",
                    CreatedUtc = DateTime.UtcNow
                };
                intent.ContentHash = ProductionContentHash.ForIntent(intent);
                await Repository.CreateIntentAsync(intent);
                var request = new CompiledMediaRequest
                {
                    Id = $"request-{Guid.NewGuid():N}", IntentSnapshotId = intent.Id,
                    CapabilityProfileId = _profile.Id, CapabilityCellId = _cell.Id,
                    CompilerId = _profile.CompilerId, CompilerVersion = _profile.CompilerVersion,
                    RequestSchemaVersion = "request-v1", ProviderKey = _profile.ProviderKey,
                    ModelId = _profile.ModelId, ModelVersion = _profile.ModelVersion,
                    WorkflowRevision = _profile.WorkflowRevision,
                    CanonicalProviderRequestJson = $"{{\"model\":\"model-1\",\"width\":1024,\"height\":1024,\"seed\":{100 + index}}}",
                    ValidationResultJson = "{\"ready\":true}", CreatedUtc = DateTime.UtcNow
                };
                request.ContentHash = ProductionContentHash.ForCompiledRequest(request, []);
                await Repository.CreateCompiledRequestAsync(request, []);
                items.Add(new ProductionWorkloadDraftItem(
                    intent.Id, request.Id, variations, "{\"maxAttempts\":2}", null,
                    new ProductionProviderEndpoint(
                        "provider-key", "endpoint-1", "https://provider.invalid/v2/endpoint-1",
                        "/run", "/status/{jobId}", "/cancel/{jobId}", 30,
                        FakeDispatchAdapter.Key, "{\"ready\":true,\"checkedUtc\":\"2026-09-02T00:00:00Z\"}"),
                    new ProductionDispatchPolicy(
                        FakeDispatchAdapter.Key, nativeVariations, 4, "worker:v1", "artifacts:v1",
                        "inline", 600),
                    new ProductionCostBasis("USD", unitCost)));
            }
            return new ProductionWorkloadDraft(
                $"workload-{Guid.NewGuid():N}", ProductionContextKind.SceneMoment,
                "session-1", "{}", "session-1", 1, "render selected moments",
                "policy-1", "{\"sourceSchema\":\"b100-v1\"}", items, DateTime.UtcNow);
        }

        public static ProductionProviderPollResult SuccessResult() => new(
            ProductionProviderJobState.Succeeded, "{\"status\":\"COMPLETED\"}",
            "{\"providerReported\":false}",
            [new ProductionProviderOutput(0, "image/png", Convert.ToBase64String([1, 2, 3, 4]), null, "{}")],
            null, null);

        public static ProductionProviderPollResult RunningResult() => new(
            ProductionProviderJobState.Running, "{\"status\":\"RUNNING\"}", "{}", [], null, null);

        public static ProductionProviderPollResult ExpiredResult() => new(
            ProductionProviderJobState.Expired, "{\"status\":\"EXPIRED\"}", "{}", [],
            "provider_result_expired", "The provider result expired before capture.");

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(_dbPath + suffix); } catch { }
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeDispatchAdapter : IProductionDispatchAdapter
    {
        public const string Key = "fake-provider-v1";
        public string AdapterKey => Key;
        public int SubmissionCount { get; private set; }
        public int CancellationCount { get; private set; }
        public bool ImmediateSuccess { get; set; }
        public bool ThrowTimeout { get; set; }
        public Dictionary<string, ProductionProviderPollResult> PollResults { get; } = [];

        public Task<IReadOnlyList<ProductionProviderSubmission>> SubmitAsync(
            ProductionDispatchGroup group,
            CancellationToken cancellationToken = default)
        {
            var results = new List<ProductionProviderSubmission>();
            foreach (var dispatch in group.Attempts)
            {
                SubmissionCount++;
                var id = $"job-{SubmissionCount}";
                var result = ImmediateSuccess ? Fixture.SuccessResult() : null;
                results.Add(new ProductionProviderSubmission(
                    dispatch.Attempt.Id, id, $"https://provider.invalid/status/{id}",
                    result?.State ?? ProductionProviderJobState.Queued,
                    result?.ProviderResponseSnapshotJson ?? "{\"status\":\"QUEUED\"}",
                    result?.CostSnapshotJson ?? "{}", result?.Outputs ?? []));
                PollResults[id] = result ?? new ProductionProviderPollResult(
                    ProductionProviderJobState.Queued, "{\"status\":\"QUEUED\"}", "{}", [], null, null);
            }
            return Task.FromResult<IReadOnlyList<ProductionProviderSubmission>>(results);
        }

        public Task<ProductionProviderPollResult> PollAsync(
            ProductionProviderEndpoint endpoint,
            string providerRequestId,
            CancellationToken cancellationToken = default)
        {
            if (ThrowTimeout) throw new TaskCanceledException("provider timeout");
            return Task.FromResult(PollResults[providerRequestId]);
        }

        public Task CancelAsync(
            ProductionProviderEndpoint endpoint,
            string providerRequestId,
            CancellationToken cancellationToken = default)
        {
            CancellationCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProductionCompiler : IProductionMediaCompiler
    {
        public ProductionMediaCompilerDescriptor Descriptor { get; } = new("compiler-1", "1", MediaOperation.Generate);

        public ProductionMediaCompilation Compile(ProductionMediaCompilationInput input)
        {
            var request = new CompiledMediaRequest
            {
                Id = input.RequestId, IntentSnapshotId = input.Intent.Id,
                CapabilityProfileId = input.CapabilityProfile.Id, CapabilityCellId = input.CapabilityCell.Id,
                CompilerId = Descriptor.CompilerId, CompilerVersion = Descriptor.CompilerVersion,
                RequestSchemaVersion = "test-v1", ProviderKey = input.CapabilityProfile.ProviderKey,
                ModelId = input.CapabilityProfile.ModelId, ModelVersion = input.CapabilityProfile.ModelVersion,
                WorkflowRevision = input.CapabilityProfile.WorkflowRevision,
                CanonicalProviderRequestJson = "{\"prompt\":\"character candidate\",\"width\":1024,\"height\":1024,\"seed\":42}",
                ValidationResultJson = "{\"ready\":true}", CreatedUtc = input.CreatedUtc
            };
            request.ContentHash = ProductionContentHash.ForCompiledRequest(request, input.ReferenceBindings);
            return new ProductionMediaCompilation(request, input.ReferenceBindings);
        }
    }

    private sealed class FakeRegisteredModelRepository : IRegisteredModelRepository
    {
        public const string ModelId = "registered-model-1";
        private static readonly RegisteredModel Model = new()
        {
            Id = ModelId,
            ProviderId = "provider-1",
            ModelIdentifier = "model-1",
            DisplayName = "Qualified identity model",
            ModelKind = ModelKind.Image,
            SupportedIdentityStrategiesJson = "[\"ReferenceConditioning\"]",
            IsEnabled = true
        };

        public Task<RegisteredModel> SaveAsync(RegisteredModel model, CancellationToken cancellationToken = default) =>
            Task.FromResult(model);

        public Task<RegisteredModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<RegisteredModel?>(string.Equals(id, ModelId, StringComparison.Ordinal) ? Model : null);

        public Task<List<RegisteredModel>> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<RegisteredModel> { Model });

        public Task<List<RegisteredModel>> GetAllEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<RegisteredModel> { Model });

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> ExistsByProviderAndIdentifierAsync(
            string providerId,
            string modelIdentifier,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeStorage : ISceneImageStorageService
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public async Task<string> SaveAsync(
            string sessionId,
            string fileName,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var path = $"{sessionId}/{fileName}";
            Files[path] = buffer.ToArray();
            return path;
        }

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Files[relativePath], writable: false));

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            Files.Remove(relativePath);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}