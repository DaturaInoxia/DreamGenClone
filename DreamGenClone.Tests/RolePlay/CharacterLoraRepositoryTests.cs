using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CharacterLoraRepositoryTests
{
    private const string AssetSha256 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string ModelSha256 = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string OutputSha256 = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

    [Fact]
    public async Task TrainingProfile_RequiresCompleteConfigurationAndQualifiesImmutably()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Repository.CreateTrainingProfileAsync(fixture.TrainingProfile());
        var qualified = await fixture.Repository.QualifyTrainingProfileAsync(
            created.Id, "{\"qualificationRunId\":\"qualification-1\",\"passed\":true}", DateTime.UtcNow);

        var persisted = await fixture.Repository.GetTrainingProfileAsync(created.Id);

        Assert.Equal(CharacterLoraTrainingProfileStatus.Qualified, persisted!.Status);
        Assert.True(persisted.Enabled);
        Assert.Equal(1, persisted.Version);
        Assert.NotNull(persisted.QualifiedUtc);
        Assert.Single(await fixture.Repository.ListTrainingProfilesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository.QualifyTrainingProfileAsync(
            created.Id, "{\"qualificationRunId\":\"qualification-2\",\"passed\":true}", DateTime.UtcNow));
    }

    [Fact]
    public async Task TrainingProfile_RejectsMissingOrSecretConfigurationWithoutDefaults()
    {
        await using var fixture = await Fixture.CreateAsync();
        var incomplete = fixture.TrainingProfile();
        incomplete.RecipeJson = "{\"rank\":16}";
        var secretBearing = fixture.TrainingProfile();
        secretBearing.Id = "training-profile-secret";
        secretBearing.Version = 2;
        secretBearing.EnvironmentRequirementsJson = "{\"apiKey\":\"must-not-persist\"}";

        var missingException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.CreateTrainingProfileAsync(incomplete));
        var secretException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.CreateTrainingProfileAsync(secretBearing));

        Assert.Contains("imageCount", missingException.Message, StringComparison.Ordinal);
        Assert.Contains("secret field", secretException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Repository.ListTrainingProfilesAsync());
    }

    [Fact]
    public async Task DatasetVersion_IsUniquePerCharacterAndTargetFamily()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Repository.CreateDatasetAsync(fixture.Dataset());

        var duplicate = fixture.Dataset();
        duplicate.Id = "dataset-duplicate";

        await Assert.ThrowsAsync<SqliteException>(() => fixture.Repository.CreateDatasetAsync(duplicate));
    }

    [Fact]
    public async Task Freeze_RequiresExactApprovedAssetsAndCreatesImmutableManifest()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dataset = await fixture.Repository.CreateDatasetAsync(fixture.Dataset());
        var trainAsset = await fixture.CreateAssetAsync("asset-train", approveForTraining: true);
        var validationAsset = await fixture.CreateAssetAsync("asset-validation", approveForTraining: true);
        var trainMember = fixture.Member(dataset.Id, trainAsset, 0,
            CharacterLoraDatasetMemberRole.IdentitySeed, CharacterLoraDatasetSplit.Train);
        var validationMember = fixture.Member(dataset.Id, validationAsset, 1,
            CharacterLoraDatasetMemberRole.Validation, CharacterLoraDatasetSplit.Validation);
        await fixture.Repository.AddDatasetMemberAsync(trainMember);
        await fixture.Repository.AddDatasetMemberAsync(validationMember);

        var frozen = await fixture.Repository.FreezeDatasetAsync(dataset.Id, "curator-1", DateTime.UtcNow);
        var members = await fixture.Repository.ListDatasetMembersAsync(dataset.Id);

        Assert.Equal(CharacterLoraDatasetStatus.Frozen, frozen.Status);
        Assert.Equal(CharacterLoraManifestHash.Compute(frozen, members), frozen.ManifestSha256);
        Assert.Equal(64, frozen.ManifestSha256!.Length);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.AddDatasetMemberAsync(fixture.Member(dataset.Id, trainAsset, 2,
                CharacterLoraDatasetMemberRole.Training, CharacterLoraDatasetSplit.Train)));
    }

    [Fact]
    public async Task Freeze_RejectsAssetWithoutLoraTrainingApproval()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dataset = await fixture.Repository.CreateDatasetAsync(fixture.Dataset());
        var trainAsset = await fixture.CreateAssetAsync("asset-train", approveForTraining: false);
        var validationAsset = await fixture.CreateAssetAsync("asset-validation", approveForTraining: true);
        await fixture.Repository.AddDatasetMemberAsync(fixture.Member(dataset.Id, trainAsset, 0,
            CharacterLoraDatasetMemberRole.IdentitySeed, CharacterLoraDatasetSplit.Train));
        await fixture.Repository.AddDatasetMemberAsync(fixture.Member(dataset.Id, validationAsset, 1,
            CharacterLoraDatasetMemberRole.Validation, CharacterLoraDatasetSplit.Validation));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.FreezeDatasetAsync(dataset.Id, "curator-1", DateTime.UtcNow));

        Assert.Contains("CharacterLoraTraining", exception.Message, StringComparison.Ordinal);
        Assert.Equal(CharacterLoraDatasetStatus.Draft,
            (await fixture.Repository.GetDatasetAsync(dataset.Id))!.Status);
    }

    [Fact]
    public async Task Freeze_RejectsMemberWhoseChecksumDoesNotMatchApprovedAsset()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dataset = await fixture.Repository.CreateDatasetAsync(fixture.Dataset());
        var trainAsset = await fixture.CreateAssetAsync("asset-train", approveForTraining: true);
        var validationAsset = await fixture.CreateAssetAsync("asset-validation", approveForTraining: true);
        var trainMember = fixture.Member(dataset.Id, trainAsset, 0,
            CharacterLoraDatasetMemberRole.IdentitySeed, CharacterLoraDatasetSplit.Train);
        trainMember.AssetSha256 = OutputSha256;
        await fixture.Repository.AddDatasetMemberAsync(trainMember);
        await fixture.Repository.AddDatasetMemberAsync(fixture.Member(dataset.Id, validationAsset, 1,
            CharacterLoraDatasetMemberRole.Validation, CharacterLoraDatasetSplit.Validation));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.FreezeDatasetAsync(dataset.Id, "curator-1", DateTime.UtcNow));
    }

    [Fact]
    public async Task Curation_UpdatesMutableFieldsOptimisticallyAndFrozenDatasetIsImmutable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dataset = await fixture.Repository.CreateDatasetAsync(fixture.Dataset());
        var trainAsset = await fixture.CreateAssetAsync("asset-curate-train", approveForTraining: true);
        var validationAsset = await fixture.CreateAssetAsync("asset-curate-validation", approveForTraining: true);
        var train = fixture.Member(dataset.Id, trainAsset, 0,
            CharacterLoraDatasetMemberRole.IdentitySeed, CharacterLoraDatasetSplit.Train);
        var validation = fixture.Member(dataset.Id, validationAsset, 1,
            CharacterLoraDatasetMemberRole.Validation, CharacterLoraDatasetSplit.Validation);
        await fixture.Repository.AddDatasetMemberAsync(train);
        await fixture.Repository.AddDatasetMemberAsync(validation);

        foreach (var member in new[] { train, validation })
        {
            var expectedRevision = member.CaptionRevision;
            member.Caption = $"curated {member.Caption}";
            member.CaptionRevision++;
            member.CurationStatus = CharacterLoraCurationStatus.Accepted;
            member.CurationFindingsJson = "{\"identityDrift\":false,\"nearDuplicate\":false,\"anatomyIssue\":false,\"leakage\":false,\"permanentTraits\":true}";
            member.ReviewedBy = "curator-1";
            member.ReviewedUtc = DateTime.UtcNow;
            await fixture.Repository.CurateDatasetMemberAsync(member, expectedRevision);
        }

        var persisted = await fixture.Repository.ListDatasetMembersAsync(dataset.Id);
        Assert.All(persisted, member => Assert.Equal(CharacterLoraCurationStatus.Accepted, member.CurationStatus));
        await fixture.Repository.FreezeDatasetAsync(dataset.Id, "curator-1", DateTime.UtcNow);
        train.CaptionRevision++;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.CurateDatasetMemberAsync(train, train.CaptionRevision - 1));
    }

    [Fact]
    public async Task DatasetMembership_RetainsDraftSharedAsset()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dataset = await fixture.Repository.CreateDatasetAsync(fixture.Dataset());
        var asset = await fixture.CreateDraftAssetAsync("asset-draft");
        var member = fixture.Member(dataset.Id, asset, 0,
            CharacterLoraDatasetMemberRole.IdentitySeed, CharacterLoraDatasetSplit.Train);
        member.SceneAssetVersion = 1;
        await fixture.Repository.AddDatasetMemberAsync(member);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Assets.DeleteAsync(asset.Id));

        Assert.Contains("in use", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await fixture.Assets.GetAsync(asset.Id));
    }

    [Fact]
    public async Task TrainingAttempts_AreAppendOnlyAndArtifactPreservesExactLineage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dataset = await fixture.CreateFrozenDatasetAsync();
        await fixture.CreateQualifiedTrainingProfileAsync();
        var job = fixture.Job(dataset.Id);
        var createdJob = await fixture.Repository.CreateTrainingJobAsync(job);
        Assert.Equal(1, createdJob.TrainingProfileVersion);
        Assert.Contains("qualificationRunId", createdJob.TrainingProfileSnapshotJson, StringComparison.Ordinal);
        await fixture.Repository.TransitionTrainingJobAsync(
            job.Id, CharacterLoraTrainingJobStatus.Draft, CharacterLoraTrainingJobStatus.Ready, 1);
        await fixture.Repository.TransitionTrainingJobAsync(
            job.Id, CharacterLoraTrainingJobStatus.Ready, CharacterLoraTrainingJobStatus.Queued, 2);
        await fixture.Repository.TransitionTrainingJobAsync(
            job.Id, CharacterLoraTrainingJobStatus.Queued, CharacterLoraTrainingJobStatus.Running, 3);
        var attempt = fixture.Attempt(job.Id);
        await fixture.Repository.CreateTrainingAttemptAsync(attempt);
        var submitted = await fixture.Repository.RecordTrainingSubmissionAsync(
            attempt.Id, "trainer-provider", "provider-job-1", "https://provider.invalid/jobs/1", 1);
        var running = await fixture.Repository.TransitionTrainingAttemptAsync(
            attempt.Id, CharacterLoraTrainingAttemptStatus.Submitted,
            CharacterLoraTrainingAttemptStatus.Running, submitted.ConcurrencyVersion);
        var succeeded = await fixture.Repository.RecordTrainingResultAsync(
            attempt.Id, "lora/output.safetensors", OutputSha256, 1024,
            "[]", "{}", "{}", "{}", running.ConcurrencyVersion);

        var duplicate = fixture.Attempt(job.Id);
        duplicate.Id = "attempt-duplicate";
        await Assert.ThrowsAsync<SqliteException>(() => fixture.Repository.CreateTrainingAttemptAsync(duplicate));
        Assert.Equal(CharacterLoraTrainingAttemptStatus.Succeeded,
            Assert.Single(await fixture.Repository.ListTrainingAttemptsAsync(job.Id)).Status);

        var artifact = fixture.Artifact(dataset, succeeded);
        await fixture.Repository.CreateArtifactAsync(artifact);
        var qualified = await fixture.Repository.SetArtifactStatusAsync(
            artifact.Id, CharacterLoraArtifactStatus.Qualified,
            "{\"evaluationRunId\":\"lora-eval-1\",\"passed\":true}", DateTime.UtcNow);

        Assert.Equal(CharacterLoraArtifactStatus.Qualified, qualified.Status);
        Assert.Contains("lora-eval-1", qualified.DecisionEvidenceJson, StringComparison.Ordinal);
        Assert.Equal(OutputSha256, (await fixture.Repository.GetArtifactAsync(artifact.Id))!.Sha256);
        Assert.Equal(artifact.Id, Assert.Single(
            await fixture.Repository.ListArtifactsAsync(dataset.CharacterProfileId)).Id);
        Assert.Empty(await fixture.Repository.ListArtifactsAsync("other-character"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository.SetArtifactStatusAsync(
            artifact.Id, CharacterLoraArtifactStatus.Rejected, "{\"passed\":false}", DateTime.UtcNow));
    }

    [Fact]
    public async Task TrainingJob_RejectsSecretBearingRecipe()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dataset = await fixture.CreateFrozenDatasetAsync();
        var job = fixture.Job(dataset.Id);
        job.RecipeJson = "{\"apiKey\":\"must-not-persist\"}";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.CreateTrainingJobAsync(job));

        Assert.Contains("secret field", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TrainingJob_RejectsSnapshotThatDiffersFromQualifiedProfile()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dataset = await fixture.CreateFrozenDatasetAsync();
        await fixture.CreateQualifiedTrainingProfileAsync();
        var job = fixture.Job(dataset.Id);
        job.RecipeJson = Fixture.RecipeJson.Replace("\"rank\":16", "\"rank\":32", StringComparison.Ordinal);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.CreateTrainingJobAsync(job));

        Assert.Contains("exactly match", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await fixture.Repository.GetTrainingJobAsync(job.Id));
    }

    [Fact]
    public async Task TrainingService_SubmitsReconcilesAndRegistersCandidateArtifact()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dataset = await fixture.CreateFrozenDatasetAsync();
        await fixture.CreateQualifiedTrainingProfileAsync();
        var adapter = new FakeTrainingAdapter();
        var service = new CharacterLoraTrainingService(
            fixture.Repository, new CharacterLoraTrainingDispatchAdapterRegistry([adapter]));

        var ready = await service.PrepareAsync(fixture.Job(dataset.Id));
        var submitted = await service.SubmitAsync(ready.Id, TrainingEndpoint(), 73190, 1);

        Assert.Equal(ready.Id, Assert.Single(await fixture.Repository.ListTrainingJobsAsync(dataset.Id)).Id);
        Assert.Equal(CharacterLoraTrainingAttemptStatus.Submitted, submitted.Status);
        Assert.Equal("provider-job-1", submitted.ProviderRequestId);
        Assert.Equal("training-provider", submitted.ProviderKey);
        Assert.Contains(dataset.ManifestSha256!, submitted.RequestSnapshotJson, StringComparison.Ordinal);

        var succeeded = await service.ReconcileAsync(submitted.Id);
        var job = await fixture.Repository.GetTrainingJobAsync(ready.Id);
        var artifact = await fixture.Repository.GetArtifactAsync($"{submitted.Id}-artifact");

        Assert.Equal(CharacterLoraTrainingAttemptStatus.Succeeded, succeeded.Status);
        Assert.Equal(CharacterLoraTrainingJobStatus.Succeeded, job!.Status);
        Assert.NotNull(artifact);
        Assert.Equal(CharacterLoraArtifactStatus.Candidate, artifact.Status);
        Assert.Equal(OutputSha256, artifact.Sha256);
        Assert.Contains("sample-1.png", artifact.TrainingManifestJson, StringComparison.Ordinal);
        Assert.Contains("checkpoint-1000", artifact.TrainingManifestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrainingService_FailedPollPersistsDiagnosticAndRetryAppendsAttempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dataset = await fixture.CreateFrozenDatasetAsync();
        await fixture.CreateQualifiedTrainingProfileAsync();
        var adapter = new FakeTrainingAdapter { FailPoll = true };
        var service = new CharacterLoraTrainingService(
            fixture.Repository, new CharacterLoraTrainingDispatchAdapterRegistry([adapter]));
        var ready = await service.PrepareAsync(fixture.Job(dataset.Id));
        var first = await service.SubmitAsync(ready.Id, TrainingEndpoint(), 100, 1);

        var failed = await service.ReconcileAsync(first.Id);
        adapter.FailPoll = false;
        var retry = await service.RetryAsync(ready.Id, TrainingEndpoint(), 200, 1);
        var attempts = await fixture.Repository.ListTrainingAttemptsAsync(ready.Id);

        Assert.Equal(CharacterLoraTrainingAttemptStatus.Failed, failed.Status);
        Assert.Equal("trainer_failed", failed.FailureCode);
        Assert.Equal(2, retry.AttemptNumber);
        Assert.Equal(200, retry.Seed);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(CharacterLoraTrainingAttemptStatus.Failed, attempts[0].Status);
        Assert.Equal(CharacterLoraTrainingAttemptStatus.Submitted, attempts[1].Status);
    }

    private static CharacterLoraTrainingEndpoint TrainingEndpoint() => new(
        FakeTrainingAdapter.Key, "training-provider", "provider-endpoint-1",
        "https://provider.invalid", "/run", "/status/{jobId}", "/cancel/{jobId}", 60);

    private sealed class FakeTrainingAdapter : ICharacterLoraTrainingDispatchAdapter
    {
        public const string Key = "fake-lora-training-v1";
        public string AdapterKey => Key;
        public bool FailPoll { get; set; }
        private int _submissionCount;

        public Task<CharacterLoraTrainingSubmission> SubmitAsync(
            CharacterLoraTrainingRequest request, CancellationToken cancellationToken = default)
        {
            var providerRequestId = $"provider-job-{++_submissionCount}";
            return Task.FromResult(new CharacterLoraTrainingSubmission(
                providerRequestId, $"https://provider.invalid/status/{providerRequestId}", "{\"status\":\"IN_QUEUE\"}"));
        }

        public Task<CharacterLoraTrainingPollResult> PollAsync(
            CharacterLoraTrainingRequest request,
            string providerRequestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FailPoll
                ? new CharacterLoraTrainingPollResult(
                    CharacterLoraTrainingProviderState.Failed, "{\"status\":\"FAILED\"}",
                    "[{\"status\":\"FAILED\"}]", "[]", "[]", "[]", null, null, null,
                    "trainer_failed", "Trainer process exited with code 1.")
                : new CharacterLoraTrainingPollResult(
                    CharacterLoraTrainingProviderState.Succeeded, "{\"status\":\"COMPLETED\"}",
                    "[{\"status\":\"COMPLETED\"}]", "[{\"path\":\"train.log\"}]",
                    "[{\"path\":\"sample-1.png\"}]", "[{\"path\":\"checkpoint-1000\"}]",
                    "lora/output.safetensors", OutputSha256, 1024, null, null));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string RecipeJson = """
            {"imageCount":24,"coverage":{"faceAngles":5,"expressions":4},"resolutionBuckets":[512,768,1024],"repeats":10,"rank":16,"alpha":16,"unetLearningRate":0.0001,"textEncoderLearningRate":0.00001,"steps":1500,"epochs":10,"captionDropout":0.05,"priorPreservation":false,"precision":"bf16"}
            """;

        private readonly string _dbPath;
        public CharacterLoraRepository Repository { get; }
        public SceneAssetRepository Assets { get; }

        private Fixture(string dbPath, CharacterLoraRepository repository, SceneAssetRepository assets)
        {
            _dbPath = dbPath;
            Repository = repository;
            Assets = assets;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"character-lora-{Guid.NewGuid():N}.db");
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={dbPath};Pooling=False"
            });
            var assets = new SceneAssetRepository(options);
            await assets.GetAsync("__schema_probe__");
            var repository = new CharacterLoraRepository(options);
            await repository.GetDatasetAsync("__schema_probe__");
            return new Fixture(dbPath, repository, assets);
        }

        public CharacterLoraDataset Dataset() => new()
        {
            Id = "dataset-1",
            CharacterProfileId = "character-1",
            IdentityPackId = "identity-pack-1",
            Version = 1,
            Status = CharacterLoraDatasetStatus.Draft,
            TriggerToken = "dgc_character_one",
            TargetModelFamily = "sdxl",
            CoveragePlanJson = "{\"angles\":[\"front\",\"profile\"],\"expressions\":[\"neutral\",\"smile\"]}",
            CurationPolicyJson = "{\"duplicateThreshold\":0.95,\"identityReviewRequired\":true}",
            CreatedUtc = DateTime.UtcNow
        };

        public async Task<SceneAsset> CreateAssetAsync(string id, bool approveForTraining)
        {
            await CreateDraftAssetAsync(id);
            return await Assets.ApproveForProductionAsync(
                id,
                "{\"source\":\"asset-manager-synthetic-generation\",\"generationAttemptId\":\"generation-1\"}",
                SceneAssetConsentState.NotApplicable,
                SceneAssetLicenseState.Confirmed,
                "application-generated",
                approveForTraining
                    ? SceneAssetApprovedUseScope.CharacterIdentity | SceneAssetApprovedUseScope.CharacterLoraTraining
                    : SceneAssetApprovedUseScope.CharacterIdentity,
                "local-adult-production",
                "{\"modelFamilies\":[\"sdxl\"]}");
        }

        public async Task<SceneAsset> CreateDraftAssetAsync(string id)
        {
            var asset = new SceneAsset
            {
                Id = id,
                Name = id,
                Kind = SceneAssetKind.PromptGenerated,
                Status = SceneAssetStatus.Complete,
                Type = SceneAssetType.CharacterFace,
                FileRelativePath = $"assets/{id}.png",
                MediaType = "image/png",
                Width = 1024,
                Height = 1024,
                ByteLength = 100,
                Sha256 = AssetSha256,
                CompletedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            await Assets.UpsertAsync(asset);
            return (await Assets.GetAsync(id))!;
        }

        public CharacterLoraDatasetMember Member(
            string datasetId,
            SceneAsset asset,
            int ordinal,
            CharacterLoraDatasetMemberRole role,
            CharacterLoraDatasetSplit split) => new()
        {
            Id = $"member-{ordinal}",
            DatasetId = datasetId,
            Ordinal = ordinal,
            SceneAssetId = asset.Id,
            SceneAssetVersion = asset.ProductionVersion ?? 1,
            AssetSha256 = asset.Sha256,
            Role = role,
            Split = split,
            Caption = $"dgc_character_one portrait coverage {ordinal}",
            CaptionRevision = 1,
            CoverageJson = $"{{\"ordinal\":{ordinal}}}",
            GenerationAttemptId = $"generation-{ordinal}",
            CurationStatus = CharacterLoraCurationStatus.Accepted,
            CurationFindingsJson = "{\"identityDrift\":false,\"duplicate\":false,\"defect\":false}",
            ReviewedBy = "curator-1",
            ReviewedUtc = DateTime.UtcNow
        };

        public async Task<CharacterLoraDataset> CreateFrozenDatasetAsync()
        {
            var dataset = await Repository.CreateDatasetAsync(Dataset());
            var train = await CreateAssetAsync("asset-train", approveForTraining: true);
            var validation = await CreateAssetAsync("asset-validation", approveForTraining: true);
            await Repository.AddDatasetMemberAsync(Member(dataset.Id, train, 0,
                CharacterLoraDatasetMemberRole.IdentitySeed, CharacterLoraDatasetSplit.Train));
            await Repository.AddDatasetMemberAsync(Member(dataset.Id, validation, 1,
                CharacterLoraDatasetMemberRole.Validation, CharacterLoraDatasetSplit.Validation));
            return await Repository.FreezeDatasetAsync(dataset.Id, "curator-1", DateTime.UtcNow);
        }

        public CharacterLoraTrainingProfile TrainingProfile() => new()
        {
            Id = "training-profile-1",
            Name = "SDXL character LoRA",
            Version = 1,
            Status = CharacterLoraTrainingProfileStatus.Draft,
            Enabled = false,
            TargetModelFamily = "sdxl",
            BaseModelId = "sdxl-base",
            BaseModelVersion = "1.0",
            BaseModelSha256 = ModelSha256,
            TrainerId = "ai-toolkit",
            TrainerVersion = "v1.0.0",
            RecipeJson = RecipeJson,
            EnvironmentRequirementsJson = "{\"cuda\":\"12.4\",\"torch\":\"2.6.0\"}",
            CheckpointCadenceJson = "{\"everySteps\":250,\"retentionCount\":3}",
            SampleCadenceJson = "{\"everySteps\":100,\"promptSetId\":\"lora-samples-v1\"}",
            CreatedUtc = DateTime.UtcNow
        };

        public async Task<CharacterLoraTrainingProfile> CreateQualifiedTrainingProfileAsync()
        {
            var profile = await Repository.CreateTrainingProfileAsync(TrainingProfile());
            return await Repository.QualifyTrainingProfileAsync(
                profile.Id, "{\"qualificationRunId\":\"qualification-1\",\"passed\":true}", DateTime.UtcNow);
        }

        public CharacterLoraTrainingJob Job(string datasetId) => new()
        {
            Id = "job-1",
            DatasetId = datasetId,
            TrainingProfileId = "training-profile-1",
            BaseModelId = "sdxl-base",
            BaseModelVersion = "1.0",
            BaseModelSha256 = ModelSha256,
            TrainerId = "ai-toolkit",
            TrainerVersion = "v1.0.0",
            RecipeJson = RecipeJson,
            EnvironmentManifestJson = "{\"cuda\":\"12.4\",\"torch\":\"2.6.0\"}",
            Status = CharacterLoraTrainingJobStatus.Draft,
            ConcurrencyVersion = 1,
            CreatedUtc = DateTime.UtcNow
        };

        public CharacterLoraTrainingAttempt Attempt(string jobId) => new()
        {
            Id = "attempt-1",
            TrainingJobId = jobId,
            AttemptNumber = 1,
            Status = CharacterLoraTrainingAttemptStatus.Pending,
            ConcurrencyVersion = 1,
            Seed = 73190,
            RequestSnapshotJson = "{\"datasetManifest\":\"dataset-1\",\"seed\":73190}",
            CreatedUtc = DateTime.UtcNow
        };

        public CharacterLoraArtifact Artifact(
            CharacterLoraDataset dataset, CharacterLoraTrainingAttempt attempt) => new()
        {
            Id = "artifact-1",
            CharacterProfileId = dataset.CharacterProfileId,
            DatasetId = dataset.Id,
            TrainingAttemptId = attempt.Id,
            Version = 1,
            BaseModelId = "sdxl-base",
            BaseModelVersion = "1.0",
            BaseModelSha256 = ModelSha256,
            TriggerToken = dataset.TriggerToken,
            FileRelativePath = attempt.OutputFileRelativePath!,
            Sha256 = attempt.OutputSha256!,
            TrainingManifestJson = "{\"datasetManifestSha256\":\"frozen\",\"trainer\":\"ai-toolkit-v1.0.0\"}",
            Status = CharacterLoraArtifactStatus.Candidate,
            CreatedUtc = DateTime.UtcNow
        };

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
