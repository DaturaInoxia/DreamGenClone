using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.ModelManager;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Operator request 2026-09-23: "it should allow for identity as reference images". A model can carry identity
/// WITHOUT a configured IP-Adapter/PuLID mechanism, by taking the approved face as a reference IMAGE — Qwen-Image-2.1
/// declares NativeMultiReference and no IdentityMechanism at all.
///
/// These tests pin the render handler's half of that contract: the approved face reaches the reference client as
/// request DATA (not as a re-derived path), the IP-Adapter path is left exactly as it was, and the two mechanisms are
/// never silently swapped — the same fail-fast rule the scene-image native-reference path follows.
/// </summary>
public sealed class SceneAssetGenerationJobHandlerIdentityReferenceTests
{
    [Fact]
    public async Task HandleAsync_NativeReferenceIdentity_SendsTheApprovedFaceAsTheOnlyReference()
    {
        var world = new World();
        var referenceClient = new RecordingReferenceClient();
        var handler = world.CreateHandler(referenceClient: referenceClient, identityClient: new NeverCalledIdentityClient());

        await handler.HandleAsync(world.JobFor(packId: "pack-1", faceAssetId: "face-1"), CancellationToken.None);

        var request = Assert.Single(referenceClient.Requests);
        var reference = Assert.Single(request.References);
        Assert.Equal(world.FaceBytes, reference.Content);
        Assert.Contains("face-1", reference.FileName, StringComparison.Ordinal);
        Assert.Contains("Front", reference.SemanticRole, StringComparison.Ordinal);
        Assert.Equal("qwen-native", world.RequestedModelId);
        Assert.Equal("1024x1536", request.Size);
        Assert.Equal(world.ImageId, request.CorrelationId);

        // The render must NEVER fall back to a prompt-only client: an unconditioned image is indistinguishable from a
        // conditioned one in the candidate list.
        Assert.Equal(0, world.PromptOnlyClient.Calls);
    }

    /// <summary>
    /// A native-reference model carries the pose as a reference image (measured 2026-09-23: 2.1 follows the
    /// skeleton's geometry even when the prompt says otherwise), so identity and pose travel in ONE call — the
    /// skeleton is appended after the face rather than refused or dropped.
    /// </summary>
    [Fact]
    public async Task HandleAsync_PoseWithNativeReferenceIdentity_AppendsTheSkeletonInsteadOfDroppingIt()
    {
        var world = new World();
        var referenceClient = new RecordingReferenceClient();
        var handler = world.CreateHandler(referenceClient: referenceClient, identityClient: new NeverCalledIdentityClient());

        await handler.HandleAsync(
            world.JobFor(packId: "pack-1", faceAssetId: "face-1", poseStance: "Standing"),
            CancellationToken.None);

        var request = Assert.Single(referenceClient.Requests);
        Assert.Equal(2, request.References.Count);
        Assert.Equal(world.FaceBytes, request.References[0].Content);
        Assert.Equal(World.SkeletonBytes, request.References[1].Content);
        Assert.Contains("pose reference", request.References[1].SemanticRole, StringComparison.Ordinal);
    }

    /// <summary>A configured mechanism is used when the model has one, so adding the native path cannot reroute it.</summary>
    [Fact]
    public async Task HandleAsync_IpAdapterIdentity_StillUsesTheIdentityClientAndTheMechanism()
    {
        var world = new World(modelId: "juggernaut-ipadapter");
        var identityClient = new RecordingIdentityClient();
        var handler = world.CreateHandler(referenceClient: new RecordingReferenceClient(), identityClient: identityClient);

        await handler.HandleAsync(world.JobFor(packId: "pack-1", faceAssetId: "face-1"), CancellationToken.None);

        var request = Assert.Single(identityClient.Requests);
        Assert.Equal(world.FaceBytes, request.ReferenceImageBytes);
        Assert.Equal("juggernaut-ipadapter", world.RequestedModelId);
    }

    /// <summary>
    /// A model that can carry identity by NEITHER mechanism fails in the render rather than producing an
    /// unconditioned image, and the reason is the resolver's own reason.
    /// </summary>
    [Fact]
    public async Task HandleAsync_IdentityOnAModelWithNoQualifiedStrategy_RefusesAndNamesTheReason()
    {
        var world = new World(modelId: "no-identity-model");
        var handler = world.CreateHandler(referenceClient: new RecordingReferenceClient(), identityClient: new NeverCalledIdentityClient());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            world.JobFor(packId: "pack-1", faceAssetId: "face-1"),
            CancellationToken.None));

        Assert.Contains("cannot carry it", error.Message, StringComparison.Ordinal);
        Assert.Contains("does not declare support for", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A canonical angle is RENDERED from two references — the accepted body (the build) and the committed angle
    /// skeleton (the turn) — in that measured order. This is the app-side half of the proof in
    /// <c>specs/image-generator-tests/qwen-21-native-reference/RUNBOOK.md</c> (cases body-angle-34-*, body-profile-*).
    /// </summary>
    [Fact]
    public async Task HandleAsync_BodyAngle_SendsTheAcceptedBodyThenTheAngleSkeleton()
    {
        var world = new World();
        var referenceClient = new RecordingReferenceClient();
        var handler = world.CreateHandler(referenceClient: referenceClient, identityClient: new NeverCalledIdentityClient());

        await handler.HandleAsync(
            world.JobForBodyAngle(SceneImageReferenceBodyView.ProfileLeft),
            CancellationToken.None);

        var request = Assert.Single(referenceClient.Requests);
        Assert.Equal(2, request.References.Count);
        Assert.Equal(World.SourceBodyBytes, request.References[0].Content);
        Assert.Equal(World.AngleSkeletonBytes, request.References[1].Content);
        Assert.Contains("accepted body", request.References[0].SemanticRole, StringComparison.Ordinal);
        Assert.Contains("angle reference", request.References[1].SemanticRole, StringComparison.Ordinal);
        Assert.Contains("ProfileLeft", request.References[1].SemanticRole, StringComparison.Ordinal);

        // No identity reference and no stance skeleton: an angle render's identity IS its accepted body and its pose
        // IS the angle skeleton, so a third input would be an input the render does not use.
        Assert.Equal(0, world.PromptOnlyClient.Calls);
    }

    /// <summary>
    /// An angle render names no accepted body: it is a refusal, never a text-to-image render that would invent a body
    /// wearing the angle's label.
    /// </summary>
    [Fact]
    public async Task HandleAsync_BodyAngleWithoutASource_RefusesRatherThanInventingABody()
    {
        var world = new World();
        var handler = world.CreateHandler(referenceClient: new RecordingReferenceClient(), identityClient: new NeverCalledIdentityClient());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            world.JobForBodyAngle(SceneImageReferenceBodyView.ThreeQuarterLeft, sourceImageId: null),
            CancellationToken.None));

        Assert.Contains("names no accepted source body", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model that carries references through an applied mechanism has no slot for a BODY image, so the render refuses
    /// with that reason instead of rendering an unconditioned image that would look like a successful angle.
    /// </summary>
    [Fact]
    public async Task HandleAsync_BodyAngleOnAModelWithoutReferenceSlots_RefusesAndNamesTheMechanism()
    {
        var world = new World(modelId: "juggernaut-ipadapter");
        var handler = world.CreateHandler(referenceClient: new RecordingReferenceClient(), identityClient: new NeverCalledIdentityClient());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            world.JobForBodyAngle(SceneImageReferenceBodyView.ThreeQuarterRight),
            CancellationToken.None));

        Assert.Contains("nowhere to go", error.Message, StringComparison.Ordinal);
        Assert.Contains(ReferenceStrategyResolver.IdentityReferenceConditioning, error.Message, StringComparison.Ordinal);
    }

    /// <summary>An angle whose skeleton is not committed is refused, and the refusal names the committed set.</summary>
    [Fact]
    public async Task HandleAsync_BodyAngleWithNoCommittedSkeleton_RefusesAndNamesTheSet()
    {
        var world = new World();
        var handler = world.CreateHandler(referenceClient: new RecordingReferenceClient(), identityClient: new NeverCalledIdentityClient());

        // The FRONT is a real canonical view but not an angle: it is the base, generated from the body card. The
        // refusal has to come from the angle library, not from a stub that happens to return bytes for any view.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            world.JobForBodyAngle(SceneImageReferenceBodyView.Front),
            CancellationToken.None));

        Assert.Contains("has no committed angle skeleton", error.Message, StringComparison.Ordinal);
        Assert.Contains("ThreeQuarterLeft", error.Message, StringComparison.Ordinal);
    }

    /// <summary>An angle value that is not a body view at all is refused before any resolution is attempted.</summary>
    [Fact]
    public async Task HandleAsync_BodyAngleThatIsNotAView_RefusesNamingTheCommittedSet()
    {
        var world = new World();
        var handler = world.CreateHandler(referenceClient: new RecordingReferenceClient(), identityClient: new NeverCalledIdentityClient());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            world.JobForBodyAngle(SceneImageReferenceBodyView.Front, angleView: "Sideways"),
            CancellationToken.None));

        Assert.Contains("is not a canonical body view", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// One asset, one prompt-generated image, an approved pack, an approved face, and a strategy resolver keyed by
    /// model id — the smallest world in which the render path's own decisions are visible.
    /// </summary>
    private sealed class World
    {
        public World(string modelId = "qwen-native")
        {
            RequestedModelId = modelId;
            AssetId = $"asset-{Guid.NewGuid():N}";
            ImageId = $"image-{Guid.NewGuid():N}";
            SourceImageId = $"source-{Guid.NewGuid():N}";
            Repository = new StubSceneAssetRepository(
                new SceneAsset { Id = AssetId, Type = SceneAssetType.CharacterBody },
                new SceneAssetImage
                {
                    Id = ImageId,
                    AssetId = AssetId,
                    Kind = SceneAssetKind.PromptGenerated,
                    Status = SceneAssetStatus.Pending,
                    Prompt = "Full-body photograph of a middle-aged woman.",
                    // A named compiler means the prompt is already model-ready, so this test is about the identity
                    // reference rather than about prompt compilation.
                    PromptCompilerId = "body-reference-v1"
                },
                // The ACCEPTED body an angle render is based on: a complete image with a stored file, which is what
                // the render re-reads rather than trusting the queued id.
                new SceneAssetImage
                {
                    Id = SourceImageId,
                    AssetId = AssetId,
                    Kind = SceneAssetKind.PromptGenerated,
                    Status = SceneAssetStatus.Complete,
                    Prompt = "Full-body photograph of a middle-aged woman.",
                    PromptCompilerId = "body-reference-v1",
                    FileRelativePath = $"assets/{SourceImageId}.png"
                });
            IdentityRepository = new StubIdentityRepository(
                new CharacterImageIdentityPack
                {
                    Id = "pack-1",
                    Status = CharacterImageIdentityPackStatus.Approved,
                    Version = 3
                },
                new SceneImageReferenceAsset
                {
                    Id = "face-1",
                    IdentityPackId = "pack-1",
                    AssetKind = SceneImageReferenceAssetKind.Face,
                    FaceView = SceneImageReferenceFaceView.Front,
                    IsApproved = true,
                    FileRelativePath = "identity/becky/face-1.png",
                    Sha256 = "FACE1HASH"
                });
            IdentityStorage = new StubIdentityStorage(FaceBytes);
        }

        public string RequestedModelId { get; }

        public string AssetId { get; }

        public string ImageId { get; }

        public string SourceImageId { get; }

        public byte[] FaceBytes { get; } = [9, 9, 9, 9];

        public static readonly byte[] SkeletonBytes = [4, 4, 4, 4];

        /// <summary>The accepted body the angle render is based on — distinct bytes, so the order of the two references
        /// is asserted rather than assumed.</summary>
        public static readonly byte[] SourceBodyBytes = [7, 7, 7, 7];

        public static readonly byte[] AngleSkeletonBytes = [5, 5, 5, 5];

        public StubSceneAssetRepository Repository { get; }

        public StubIdentityRepository IdentityRepository { get; }

        public StubIdentityStorage IdentityStorage { get; }

        public RecordingPromptOnlyClient PromptOnlyClient { get; } = new();

        public BackgroundJobEnvelope JobFor(string packId, string faceAssetId, string? poseStance = null)
            => new()
            {
                JobType = BackgroundJobTypes.SceneAssetGeneration,
                PayloadJson = JsonSerializer.Serialize(new SceneAssetGenerationJobPayload
                {
                    AssetId = AssetId,
                    ImageId = ImageId,
                    ModelId = RequestedModelId,
                    ImageSize = "1024x1536",
                    IdentityPackId = packId,
                    IdentityFaceAssetId = faceAssetId,
                    PoseStance = poseStance,
                    PoseStrength = poseStance is null ? null : 0.6
                })
            };

        /// <summary>
        /// An angle render's request: the accepted body it is based on, and the canonical angle asked for. The source
        /// is passed as a null to exercise the refusal for a request that names none.
        /// </summary>
        public BackgroundJobEnvelope JobForBodyAngle(
            SceneImageReferenceBodyView view, string? sourceImageId = "source", string? angleView = null)
            => new()
            {
                JobType = BackgroundJobTypes.SceneAssetGeneration,
                PayloadJson = JsonSerializer.Serialize(new SceneAssetGenerationJobPayload
                {
                    AssetId = AssetId,
                    ImageId = ImageId,
                    ModelId = RequestedModelId,
                    ImageSize = "1024x1536",
                    BodyAngleView = angleView ?? view.ToString(),
                    BodyAngleSourceImageId = sourceImageId == "source" ? SourceImageId : sourceImageId
                })
            };

        public SceneAssetGenerationJobHandler CreateHandler(
            IReferenceConditionedImageClient referenceClient,
            IIdentityConditionedImageClient identityClient)
            => new(
                Repository,
                new StubSceneAssetStorage(),
                new StubModelResolution(RequestedModelId),
                PromptOnlyClient,
                // Pose clients are unreachable on the native path (the skeleton travels as a reference image), so
                // leaving them null is the assertion that no ControlNet work happens. The skeleton PROVIDER is real
                // for this path, because the committed stance skeleton is what gets appended.
                poseClient: null!,
                poseResolver: null!,
                skeletons: new StubSkeletons(),
                // The angle skeletons answer a different question (which VIEW) and are only read by an angle render.
                angleSkeletons: new StubAngleSkeletons(),
                identityClient,
                referenceClient,
                new StubReferenceStrategies(),
                IdentityRepository,
                IdentityStorage,
                NullLogger<SceneAssetGenerationJobHandler>.Instance,
                // The shared pack -> approved face resolver, constructed over the same stub repository so the
                // production validation (approval, ownership, face kind) is the code under test.
                new IdentityFaceReferenceResolver(IdentityRepository));
    }

    private sealed class StubSceneAssetRepository(SceneAsset asset, params SceneAssetImage[] images) : ISceneAssetRepository
    {
        public Task<SceneAsset?> GetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<SceneAsset?>(asset.Id == assetId ? asset : null);

        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
            => Task.FromResult(images.FirstOrDefault(candidate => candidate.Id == imageId));

        public Task UpsertImageAsync(SceneAssetImage value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateCandidateFieldsAsync(
            string assetId, string? candidateBatchId, SceneAssetCandidateDecision? candidateDecision,
            string? candidateNotes, string? candidateSourceAssetId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<SceneAsset>> ListAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAsset>> ListByPackAsync(string identityPackId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAsset>> ListByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetImageCandidateDecisionAsync(
            string imageId, SceneAssetCandidateDecision decision, string? notes, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> ApproveImageForProductionAsync(
            string imageId, string sourceProvenanceJson, SceneAssetConsentState consentState,
            SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope,
            string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpsertAsync(SceneAsset value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<SceneAsset> ApproveForProductionAsync(
            string assetId, string sourceProvenanceJson, SceneAssetConsentState consentState,
            SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope,
            string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CreatePromotedAsync(SceneAsset value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubSceneAssetStorage : ISceneAssetStorageService
    {
        public Task<StoredSceneAsset> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
            => Task.FromResult(new StoredSceneAsset($"assets/{fileName}", 4, "SHA", "image/png", 8, 8));

        // The accepted body an angle render is based on is read back through this service, so it returns the same
        // bytes the world holds as its source body.
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream(World.SourceBodyBytes));

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubSkeletons : IStancePoseSkeletonProvider
    {
        public Task<byte[]> ReadAsync(BodyReferenceStance stance, CancellationToken cancellationToken = default)
            => Task.FromResult(World.SkeletonBytes);

        public string FileNameFor(BodyReferenceStance stance) => $"{stance}.png";
    }

    private sealed class StubAngleSkeletons : IBodyAngleSkeletonProvider
    {
        public Task<byte[]> ReadAsync(SceneImageReferenceBodyView view, CancellationToken cancellationToken = default)
            => Task.FromResult(World.AngleSkeletonBytes);

        public string FileNameFor(SceneImageReferenceBodyView view) => $"angle-{view}.png";
    }

    private sealed class StubIdentityRepository(
        CharacterImageIdentityPack pack,
        SceneImageReferenceAsset asset) : ICharacterImageIdentityRepository
    {
        public Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken = default)
            => Task.FromResult<CharacterImageIdentityPack?>(pack.Id == packId ? pack : null);

        public Task<SceneImageReferenceAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<SceneImageReferenceAsset?>(asset.Id == assetId ? asset : null);

        public Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(
            string characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack?> GetLatestApprovedPackAsync(
            string characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> UpsertDraftAsync(
            CharacterImageIdentityPack value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> ApproveAsync(
            string packId, string descriptorSnapshotJson, string canonicalFaceAssetId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> SupersedeAsync(string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeletePackAsync(string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddAssetAsync(SceneImageReferenceAsset value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(
            string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAssetProvenanceAsync(
            string assetId, string sourceLabel, SceneImageReferenceConsentState consentState,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetAssetApprovalAsync(
            string assetId, bool isApproved, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAssetQualityAsync(
            string assetId, SceneImageReferenceQuality qualityRating, string qualityNotes,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountAssetsByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubIdentityStorage(byte[] bytes) : ICharacterImageAssetStorageService
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream(bytes));

        public Task<StoredCharacterImageAsset> SaveAsync(
            string characterProfileId, string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubModelResolution(string modelId) : IModelResolutionService
    {
        public Task<ResolvedImageModel> ResolveImageModelByIdAsync(
            string requestedModelId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ResolvedImageModel(
                "http://localhost",
                "/prompt",
                300,
                null,
                "qwen_image_2.1_int8_convrot.safetensors",
                ImageContentPolicy.AdultAllowed,
                "Local",
                false,
                SceneImageModelFamily.QwenImage21,
                SceneImagePromptDialect.NaturalLanguage,
                ImageProtocol.ComfyUi));

        /// <summary>
        /// A native-reference model has NO mechanism, so asking for one here is the mistake the feature exists to
        /// avoid: the failure is loud, which is how these tests prove the native path never consults it.
        /// </summary>
        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelByIdAsync(
            string requestedModelId, CancellationToken cancellationToken = default)
            => requestedModelId == "juggernaut-ipadapter"
                ? Task.FromResult(new ResolvedIdentityImageModel(
                    "http://localhost",
                    300,
                    "juggernautXL_ragnarok.safetensors",
                    ImageContentPolicy.AdultAllowed,
                    "Local",
                    SceneImageIdentityMechanism.IpAdapter,
                    "PLUS FACE (portraits)",
                    null,
                    0.8))
                : throw new ModelResolutionException(
                    $"Identity mechanism not configured or unknown for model '{requestedModelId}'.");

        public Task<ResolvedModel> ResolveAsync(
            AppFunction function, string? sessionModelId = null, double? sessionTemperature = null,
            double? sessionTopP = null, int? sessionMaxTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedModel> ResolveImagePromptModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedImageModel> ResolveImageModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneImageModelChoice>> ListSceneImageModelsAsync(
            bool identityCapableOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// The strategy contract, as a model row declares it: "qwen-native" carries identity by native references and
    /// "juggernaut-ipadapter" by a configured mechanism, and anything else declares neither.
    /// </summary>
    private sealed class StubReferenceStrategies : IReferenceStrategyResolver
    {
        public Task<ReferenceStrategyResolution> ResolveAsync(
            string modelId, string strategy, CancellationToken cancellationToken = default)
        {
            var available = (modelId, strategy) switch
            {
                ("qwen-native", ReferenceStrategyResolver.IdentityNativeMultiReference) => true,
                ("juggernaut-ipadapter", ReferenceStrategyResolver.IdentityReferenceConditioning) => true,
                _ => false
            };

            return Task.FromResult(new ReferenceStrategyResolution(
                available ? ReferenceStrategyResolutionStatus.Possible : ReferenceStrategyResolutionStatus.Unqualified,
                strategy,
                available
                    ? $"'{strategy}' is declared and qualified for model '{modelId}'."
                    : $"Model '{modelId}' does not declare support for '{strategy}' in Model Manager."));
        }
    }

    private sealed class RecordingReferenceClient : IReferenceConditionedImageClient
    {
        public List<ReferenceConditionedImageRequest> Requests { get; } = [];

        public Task<byte[]> GenerateWithReferencesAsync(
            ResolvedImageModel model, ReferenceConditionedImageRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult<byte[]>([1, 2, 3]);
        }
    }

    private sealed class RecordingIdentityClient : IIdentityConditionedImageClient
    {
        public List<IdentityControlledImageRequest> Requests { get; } = [];

        public Task<byte[]> GenerateAsync(
            ResolvedIdentityImageModel model, IdentityControlledImageRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult<byte[]>([1, 2, 3]);
        }
    }

    private sealed class NeverCalledIdentityClient : IIdentityConditionedImageClient
    {
        public Task<byte[]> GenerateAsync(
            ResolvedIdentityImageModel model, IdentityControlledImageRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "The IP-Adapter client must not be reached for a native-reference identity render.");
    }

    private sealed class RecordingPromptOnlyClient : IImageGenerationClient
    {
        public int Calls { get; private set; }

        public Task<byte[]> GenerateAsync(
            ResolvedImageModel model, string prompt, string? negativePrompt, string? size,
            long? seed, CancellationToken cancellationToken = default, SceneImageGenerationOptions? options = null)
        {
            Calls++;
            return Task.FromResult<byte[]>([1, 2, 3]);
        }

        public Task<(bool Success, string Message)> CheckImageModelHealthAsync(
            string baseUrl, string modelIdentifier, int timeoutSeconds, string? apiKey,
            string endpoint, ImageContentPolicy contentPolicy, CancellationToken cancellationToken = default,
            ImageProtocol protocol = ImageProtocol.ComfyUi)
            => throw new NotSupportedException();
    }
}
