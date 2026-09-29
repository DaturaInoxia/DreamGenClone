using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The step between "the operator accepted a cell image" and "the dataset can be frozen".
///
/// <para>
/// It is pinned here because the two facts are easy to confuse and only one of them trains: the cell deck writes a
/// candidate decision on an image, while freezing verifies a Scene ASSET that is complete, approved FOR
/// <see cref="SceneAssetApprovedUseScope.CharacterLoraTraining"/>, and carries the exact version and checksum the
/// member claims. The final assertion is the point of the whole file — after registration the dataset FREEZES, which
/// is what a fully rendered and fully curated set could not do before.
/// </para>
/// </summary>
public sealed class CharacterLoraDatasetRegistrationTests
{
    private const string DatasetId = "dataset-registration-1";
    private const string TriggerToken = "dgc_character_one";
    private const string ImageSha256 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task Register_PromotesEachAcceptedCellIntoAnApprovedAssetAndTheDatasetThenFreezes()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddAttempt("core.front.cu.1", "image-1", SceneAssetCandidateDecision.Accepted);
        fixture.AddAttempt("core.front.cu.2", "image-2", SceneAssetCandidateDecision.Accepted);
        fixture.AddAttempt("var.front.cu.1", "image-3", SceneAssetCandidateDecision.Accepted);

        var preview = await fixture.Service.PreviewAsync(DatasetId);
        var outcome = await fixture.Service.RegisterAsync(DatasetId, fixture.Facts());
        var members = await fixture.Repository.ListDatasetMembersAsync(DatasetId);
        var frozen = await fixture.Repository.FreezeDatasetAsync(DatasetId, "curator-1", DateTime.UtcNow);

        Assert.Equal(3, preview.Ready.Count);
        Assert.Empty(preview.Refused);
        Assert.Equal(3, outcome.RegisteredCount);

        Assert.Equal(CharacterLoraDatasetStatus.Frozen, frozen.Status);
        Assert.Equal(3, members.Count);
        Assert.All(members, member => Assert.Equal(CharacterLoraCurationStatus.Accepted, member.CurationStatus));
        Assert.All(members, member => Assert.Contains(TriggerToken, member.Caption, StringComparison.Ordinal));

        // The identity seed is the first face-visible TRAIN cell in plan order, and a freeze requires exactly one.
        Assert.Single(members.Where(member => member.Role == CharacterLoraDatasetMemberRole.IdentitySeed));
        Assert.Contains(
            "\"cellKey\":\"core.front.cu.1\"",
            members.Single(member => member.Role == CharacterLoraDatasetMemberRole.IdentitySeed).CoverageJson,
            StringComparison.Ordinal);
        Assert.Contains(members, member => member.Split == CharacterLoraDatasetSplit.Validation);

        // Every promoted asset shares the accepted image's bytes and is approved for LoRA training specifically.
        foreach (var member in members)
        {
            var asset = await fixture.Assets.GetAsync(member.SceneAssetId);
            Assert.NotNull(asset);
            Assert.Equal(SceneAssetStatus.Complete, asset.Status);
            Assert.Equal(ImageSha256, asset.Sha256);
            Assert.Equal(member.AssetSha256, asset.Sha256);
            Assert.Equal(member.SceneAssetVersion, asset.ProductionVersion);
            Assert.Equal(SceneAssetProductionApprovalStatus.Approved, asset.ProductionApprovalStatus);
            Assert.True(asset.ApprovedUseScope!.Value.HasFlag(SceneAssetApprovedUseScope.CharacterLoraTraining));
            Assert.Equal(SceneAssetConsentState.NotApplicable, asset.ConsentState);
            Assert.Equal(SceneAssetLicenseState.Confirmed, asset.LicenseState);
        }

        Assert.Equal(
            CharacterLoraManifestHash.Compute(frozen, members),
            frozen.ManifestSha256);
    }

    [Fact]
    public async Task Preview_RefusesACellWithMoreThanOneAcceptedImageAndRegistersNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddAttempt("core.front.cu.1", "image-1", SceneAssetCandidateDecision.Accepted);
        fixture.AddAttempt("core.front.cu.1", "image-1b", SceneAssetCandidateDecision.Accepted);

        var preview = await fixture.Service.PreviewAsync(DatasetId);

        Assert.Single(preview.Refused);
        Assert.Contains("core.front.cu.1", preview.Refused[0].CellKey, StringComparison.Ordinal);
        Assert.DoesNotContain(preview.Ready, item => item.CellKey == "core.front.cu.1");

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterAsync(DatasetId, fixture.Facts()));

        Assert.Contains("ambiguous", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Repository.ListDatasetMembersAsync(DatasetId));
    }

    [Fact]
    public async Task Preview_ReportsAnUnacceptedCellAsPendingRatherThanFailingTheWholeSet()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddAttempt("core.front.cu.1", "image-1", SceneAssetCandidateDecision.Accepted);
        fixture.AddAttempt("var.front.cu.1", "image-3", SceneAssetCandidateDecision.Accepted);
        fixture.AddAttempt("core.front.cu.2", "image-2", SceneAssetCandidateDecision.Undecided);

        var preview = await fixture.Service.PreviewAsync(DatasetId);
        var outcome = await fixture.Service.RegisterAsync(DatasetId, fixture.Facts());

        Assert.Equal(2, preview.Ready.Count);
        Assert.Single(preview.Pending);
        Assert.Equal("core.front.cu.2", preview.Pending[0].CellKey);
        Assert.Equal(2, outcome.RegisteredCount);
        Assert.Single(outcome.Pending);
    }

    [Fact]
    public async Task Register_RefusesAnApprovalThatDoesNotGrantCharacterLoraTraining()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddAttempt("core.front.cu.1", "image-1", SceneAssetCandidateDecision.Accepted);
        var facts = fixture.Facts() with { ApprovedUseScope = SceneAssetApprovedUseScope.CharacterIdentity };

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterAsync(DatasetId, facts));

        Assert.Contains("CharacterLoraTraining", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.Repository.ListDatasetMembersAsync(DatasetId));
    }

    [Fact]
    public async Task Register_RefusesUnrecordedConsentInsteadOfGuessingOne()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddAttempt("core.front.cu.1", "image-1", SceneAssetCandidateDecision.Accepted);
        var facts = fixture.Facts() with { ConsentState = SceneAssetConsentState.Unknown };

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterAsync(DatasetId, facts));

        Assert.Contains("Consent", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.Repository.ListDatasetMembersAsync(DatasetId));
    }

    [Fact]
    public async Task Register_IsIdempotentSoASecondPressCannotDuplicateTheSet()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddAttempt("core.front.cu.1", "image-1", SceneAssetCandidateDecision.Accepted);
        fixture.AddAttempt("core.front.cu.2", "image-2", SceneAssetCandidateDecision.Accepted);
        fixture.AddAttempt("var.front.cu.1", "image-3", SceneAssetCandidateDecision.Accepted);

        var first = await fixture.Service.RegisterAsync(DatasetId, fixture.Facts());
        var second = await fixture.Service.RegisterAsync(DatasetId, fixture.Facts());
        var members = await fixture.Repository.ListDatasetMembersAsync(DatasetId);

        Assert.Equal(3, first.RegisteredCount);
        Assert.Equal(0, second.RegisteredCount);
        Assert.Equal(3, members.Count);
        Assert.Equal(3, members.Select(member => member.Ordinal).Distinct().Count());

        // The second press reports every cell as already registered rather than silently doing nothing.
        var preview = await fixture.Service.PreviewAsync(DatasetId);
        Assert.Empty(preview.Ready);
        Assert.Equal(3, preview.Registered.Count);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _dbPath;
        private readonly Dictionary<string, List<SceneAssetImage>> _attempts = new(StringComparer.Ordinal);

        public CharacterLoraRepository Repository { get; }
        public SceneAssetRepository Assets { get; }
        public ICharacterLoraDatasetRegistrationService Service { get; private set; } = null!;

        private Fixture(string dbPath, CharacterLoraRepository repository, SceneAssetRepository assets)
        {
            _dbPath = dbPath;
            Repository = repository;
            Assets = assets;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"lora-registration-{Guid.NewGuid():N}.db");
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={dbPath};Pooling=False"
            });
            var assets = new SceneAssetRepository(options);
            await assets.GetAsync("__schema_probe__");
            var repository = new CharacterLoraRepository(options);
            await repository.GetDatasetAsync("__schema_probe__");

            // The service is built over THIS fixture's attempt dictionary, so AddAttempt/1 in a test is what the
            // service reads. A second fixture instance would mean two dictionaries and a silently empty preview.
            var fixture = new Fixture(dbPath, repository, assets);
            fixture.Service = new CharacterLoraDatasetRegistrationService(
                repository, new StubCellService(fixture._attempts), assets, new StubTemplateService());
            await repository.CreateDatasetAsync(Dataset());
            return fixture;
        }

        public void AddAttempt(string cellKey, string imageId, SceneAssetCandidateDecision decision)
        {
            if (!_attempts.TryGetValue(cellKey, out var list))
            {
                list = [];
                _attempts[cellKey] = list;
            }

            list.Add(new SceneAssetImage
            {
                Id = imageId,
                AssetId = "container-asset",
                Kind = SceneAssetKind.PromptGenerated,
                Status = SceneAssetStatus.Complete,
                Prompt = "a photorealistic photograph",
                FileRelativePath = $"assets/{imageId}.png",
                MediaType = "image/png",
                Width = 1024,
                Height = 1024,
                ByteLength = 4096,
                Sha256 = ImageSha256,
                CandidateDecision = decision,
                CreatedUtc = DateTime.UtcNow,
                CompletedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
        }

        public LoraDatasetApprovalFacts Facts() => new(
            "{\"source\":\"lora-cell-acceptance\",\"datasetId\":\"" + DatasetId + "\"}",
            SceneAssetConsentState.NotApplicable,
            SceneAssetLicenseState.Confirmed,
            "application-generated",
            SceneAssetApprovedUseScope.CharacterLoraTraining,
            "local-adult-production",
            "{\"modelFamilies\":[\"" + nameof(SceneImageModelFamily.QwenImage21) + "\"]}",
            "curator-1");

        private static CharacterLoraDataset Dataset() => new()
        {
            Id = DatasetId,
            CharacterTemplateId = "character-1",
            IdentityPackId = "identity-pack-1",
            Version = 1,
            Status = CharacterLoraDatasetStatus.Draft,
            TriggerToken = TriggerToken,
            TargetModelFamily = nameof(SceneImageModelFamily.QwenImage21),
            CoveragePlanJson = Plan().ToJson(),
            CurationPolicyJson = "{\"minimumTrainMembers\":1}",
            CreatedUtc = DateTime.UtcNow
        };

        /// <summary>
        /// A three-cell plan, all front close-ups so the stance rule is exercised the same way the real plan does
        /// (a close-up carries NO stance). The outfit key is asked of the workflow keys rather than spelled here, so
        /// the fixture cannot disagree with the frame-scoped rule it is meant to satisfy: a cell declares its outfit
        /// in the FULL-BODY vocabulary and the frame decides what phrasing it can honestly show.
        /// </summary>
        private static CoveragePlan Plan()
        {
            var outfitClose = LoraCellWorkflowKeys.OutfitKeyForDistance(
                LoraCellWorkflowKeys.VocabularyOutfitCasual, LoraCoverageDistance.CloseUp);

            var plan = new CoveragePlan
            {
                SchemaVersion = CoveragePlan.CurrentSchemaVersion,
                CharacterProfileId = "character-1",
                IdentityPackId = "identity-pack-1",
                IdentityPackVersion = 1,
                TriggerToken = TriggerToken,
                TargetModelFamily = nameof(SceneImageModelFamily.QwenImage21),
                SeedRangeStart = 41000,
                GeneratedUtc = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc),
                Vocabulary = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [LoraCellWorkflowKeys.VocabularyFacingCamera] = "facing the camera straight on",
                    [LoraCellWorkflowKeys.VocabularyWardrobeClothed] = "clothed",
                    [LoraCellWorkflowKeys.VocabularyWardrobeUnclothed] = "nude",
                    [LoraCellWorkflowKeys.VocabularyAngleFront] = "front view",
                    [LoraCellWorkflowKeys.VocabularyDistanceClose] = "close-up",
                    [LoraCellWorkflowKeys.VocabularyExpressionNeutral] = "a neutral relaxed expression",
                    [LoraCellWorkflowKeys.VocabularyLightingIndoorDim] = "dim indoor lighting with soft shadows",
                    [LoraCellWorkflowKeys.VocabularyBackgroundPlainWall] = "a plain neutral wall",
                    [outfitClose] = "wearing a plain t-shirt and jeans"
                }
            };

            plan.Records.Add(Record("core.front.cu.1", LoraCoverageCellRole.Core, CharacterLoraDatasetSplit.Train, 41000, outfitClose));
            plan.Records.Add(Record("core.front.cu.2", LoraCoverageCellRole.Core, CharacterLoraDatasetSplit.Train, 41001, outfitClose));
            plan.Records.Add(Record("var.front.cu.1", LoraCoverageCellRole.Variation, CharacterLoraDatasetSplit.Validation, 41100, outfitClose));
            return plan;
        }

        private static CoverageRecord Record(
            string key,
            LoraCoverageCellRole role,
            CharacterLoraDatasetSplit split,
            int seed,
            string outfitKey) => new()
            {
                Key = key,
                Role = role,
                AngleFamily = LoraCoverageAngleFamily.Front,
                AngleYawDeg = 0,
                FaceVisible = true,
                FaceCanonicalSlot = SceneImageReferenceFaceView.Front,
                BodyCanonicalSlot = SceneImageReferenceBodyView.Front,
                BodyState = SceneImageReferenceBodyState.Clothed,
                Distance = LoraCoverageDistance.CloseUp,
                // The real seeded curation policy's close-up aspect (global policy row), not an invented size.
                Aspect = "1024x1024",
                WardrobeState = LoraCoverageWardrobeState.Clothed,
                PoseClass = null,
                ExpressionKey = LoraCellWorkflowKeys.VocabularyExpressionNeutral,
                LightingKey = LoraCellWorkflowKeys.VocabularyLightingIndoorDim,
                BackgroundKey = LoraCellWorkflowKeys.VocabularyBackgroundPlainWall,
                OutfitKey = outfitKey,
                Split = split,
                Seed = seed
            };

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
            try
            {
                if (File.Exists(_dbPath))
                {
                    File.Delete(_dbPath);
                }
            }
            catch (IOException)
            {
                // A temp database that cannot be deleted is not a test failure.
            }
        }
    }

    private sealed class StubCellService : ICharacterLoraCellService
    {
        private readonly Dictionary<string, List<SceneAssetImage>> _attempts;

        public StubCellService(Dictionary<string, List<SceneAssetImage>> attempts) => _attempts = attempts;

        public Task<IReadOnlyList<SceneAssetImage>> ListCellAttemptsAsync(
            string datasetId, string cellKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SceneAssetImage>>(
                _attempts.TryGetValue(cellKey, out var list) ? list : []);

        public Task<SceneAssetImage> RenderCellAsync(
            string datasetId,
            string cellKey,
            string prompt,
            string modelId,
            string aspect,
            IReadOnlyList<ReferenceApplicationSelection> referenceApplications,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string CellBatchIdFor(string datasetId, string cellKey) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReferenceApplicationSelection>> ResolveCellBindingsAsync(string datasetId, string cellKey, string characterKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> DescribeIdentityReferenceAsync(string datasetId, string cellKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> DescribeBodyReferenceAsync(string datasetId, string cellKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DiscardAttemptAsync(string imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetAttemptDecisionAsync(string imageId, SceneAssetCandidateDecision decision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> ResolveCellModelAsync(string characterId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveCellModelAsync(string characterId, string modelId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubTemplateService : IImageWorkflowTemplateService
    {
        private const string CaptionTemplate =
            "{TriggerToken}, {Wardrobe}, {Angle}, {Distance}, {Expression}, {Lighting}, {Background}";

        public Task<ImageWorkflowPromptTemplate> ResolveAsync(
            string key, string? characterProfileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImageWorkflowPromptTemplate
            {
                Id = ImageWorkflowPromptTemplate.ComputeId(key, ImageWorkflowPromptTemplateScope.Global, null),
                Key = key,
                Scope = ImageWorkflowPromptTemplateScope.Global,
                Body = CaptionTemplate
            });

        public Task<ImageWorkflowPromptTemplate> ResetToSeedAsync(
            string key,
            ImageWorkflowPromptTemplateScope scope,
            string? characterProfileId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ImageWorkflowPromptTemplate>> ListTemplatesAsync(string? characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveTemplateAsync(ImageWorkflowPromptTemplate template, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReferenceWorkflowSettings> ResolveSettingsAsync(string? characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveSettingsAsync(ReferenceWorkflowSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
