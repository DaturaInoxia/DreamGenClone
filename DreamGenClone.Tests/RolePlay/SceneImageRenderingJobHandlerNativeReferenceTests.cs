using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The native-reference render path (Qwen-Image-2.1): it must never degrade into a prompt-only render
/// and must never drop the references. These tests pin the fail-fast contract on the render handler
/// itself, using a real repository so the persisted failure state is asserted too.
/// </summary>
public sealed class SceneImageRenderingJobHandlerNativeReferenceTests
{
    [Fact]
    public async Task HandleAsync_NativeReferenceWithoutAnyReferences_FailsFastAndNeverCallsAPromptOnlyClient()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewImage();
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();

        var handler = fixture.CreateHandler(referenceClient);
        var job = fixture.JobFor(image);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(job, CancellationToken.None));

        Assert.Contains("at least one reference image", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, referenceClient.Calls);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);

        var persisted = await fixture.Repository.GetImageAsync(image.Id);
        Assert.NotNull(persisted);
        Assert.Equal(SceneImageStatus.Failed, persisted!.Status);
        Assert.Contains("at least one reference image", persisted.ErrorMessage!, StringComparison.Ordinal);
        Assert.Null(persisted.FileRelativePath);
    }

    [Fact]
    public async Task HandleAsync_PoseOnNativeCapableModel_TravelsAsAReferenceAndNeverUsesTheControlNetPath()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewImage();
        image.SettingsJson = """{"poseReference":{"storagePath":"poses/standing-open.png","strength":1.0}}""";
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();

        var handler = fixture.CreateHandler(
            referenceClient,
            new StubReferenceStrategies(native: true),
            new StubSceneImageStorage(SkeletonBytes));

        await handler.HandleAsync(fixture.JobFor(image), CancellationToken.None);

        // Measured 2026-09-23: a native-reference model reads an OpenPose skeleton as pose guidance, so the pose
        // travels as one more reference image instead of forcing the ControlNet graph.
        var request = Assert.Single(referenceClient.Requests);
        var reference = Assert.Single(request.References);
        Assert.Equal(SkeletonBytes, reference.Content);
        Assert.Contains("pose reference", reference.SemanticRole, StringComparison.Ordinal);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);
    }

    [Fact]
    public async Task HandleAsync_PoseOnAModelThatQualifiesNoPoseRoute_FailsFastWithTheReason()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewImage();
        image.SettingsJson = """{"poseReference":{"storagePath":"poses/standing-open.png","strength":1.0}}""";
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();

        var handler = fixture.CreateHandler(
            referenceClient,
            new StubReferenceStrategies(native: false),
            new StubSceneImageStorage(SkeletonBytes));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(fixture.JobFor(image), CancellationToken.None));

        Assert.Contains("cannot carry it", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, referenceClient.Calls);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);
    }

    [Fact]
    public async Task HandleAsync_IdentityAndPoseOnNativeCapableModel_TravelInOneCall()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewIdentityImage();
        image.SettingsJson = """{"poseReference":{"storagePath":"poses/standing-open.png","strength":0.8}}""";
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face) = ApprovedPackAndFace();

        var handler = fixture.CreateIdentityHandler(
            new StubReferenceStrategies(native: true), referenceClient, FaceBytes, pack, face,
            new StubSceneImageStorage(SkeletonBytes));

        await handler.HandleAsync(fixture.JobFor(image), CancellationToken.None);

        var request = Assert.Single(referenceClient.Requests);
        Assert.Equal(2, request.References.Count);
        Assert.Equal(FaceBytes, request.References[0].Content);
        Assert.Equal(SkeletonBytes, request.References[1].Content);
        Assert.Contains("pose reference", request.References[1].SemanticRole, StringComparison.Ordinal);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);
    }

    [Fact]
    public async Task HandleAsync_IdentityRenderOnNativeCapableModel_SendsTheApprovedFaceAndNeverTheMechanismPath()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewIdentityImage();
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face) = ApprovedPackAndFace();

        var handler = fixture.CreateIdentityHandler(
            new StubReferenceStrategies(native: true), referenceClient, FaceBytes, pack, face);

        await handler.HandleAsync(fixture.JobFor(image), CancellationToken.None);

        var request = Assert.Single(referenceClient.Requests);
        var reference = Assert.Single(request.References);
        Assert.Equal(FaceBytes, reference.Content);
        Assert.Equal("face-1.png", reference.FileName);
        Assert.Contains("Becky", reference.SemanticRole, StringComparison.Ordinal);
        Assert.Equal(SceneImageReferenceFaceView.Front, face.FaceView);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);

        var persisted = await fixture.Repository.GetImageAsync(image.Id);
        Assert.Equal(SceneImageStatus.Complete, persisted!.Status);
        Assert.Equal("session-1/" + image.Id + ".png", persisted.FileRelativePath);
    }

    [Fact]
    public async Task HandleAsync_IdentityRenderWithUnapprovedPack_FailsFastAndNeverCallsAClient()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewIdentityImage();
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face) = ApprovedPackAndFace();
        pack.Status = CharacterImageIdentityPackStatus.Draft;

        var handler = fixture.CreateIdentityHandler(
            new StubReferenceStrategies(native: true), referenceClient, FaceBytes, pack, face);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(fixture.JobFor(image), CancellationToken.None));

        Assert.Contains("not Approved", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, referenceClient.Calls);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);

        var persisted = await fixture.Repository.GetImageAsync(image.Id);
        Assert.Equal(SceneImageStatus.Failed, persisted!.Status);
    }

    [Fact]
    public async Task HandleAsync_IdentityRenderWithoutAnyIdentityCapability_FailsFastWithTheReason()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewIdentityImage();
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face) = ApprovedPackAndFace();

        var handler = fixture.CreateIdentityHandler(
            new StubReferenceStrategies(native: false), referenceClient, FaceBytes, pack, face);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(fixture.JobFor(image), CancellationToken.None));

        Assert.Contains("cannot carry it", exception.Message, StringComparison.Ordinal);
        Assert.Contains("does not declare support", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, referenceClient.Calls);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);
    }

    private static readonly byte[] FaceBytes = [9, 8, 7];

    private static readonly byte[] SkeletonBytes = [5, 5, 5, 5];

    private static (CharacterImageIdentityPack Pack, SceneImageReferenceAsset Face) ApprovedPackAndFace()
        => (new CharacterImageIdentityPack
        {
            Id = "pack-1",
            CharacterTemplateId = "character-1",
            Version = 3,
            Status = CharacterImageIdentityPackStatus.Approved,
            CanonicalFaceAssetId = "face-1"
        },
        new SceneImageReferenceAsset
        {
            Id = "face-1",
            IdentityPackId = "pack-1",
            AssetKind = SceneImageReferenceAssetKind.Face,
            IsApproved = true,
            FaceView = SceneImageReferenceFaceView.Front,
            FileRelativePath = "identity/face-1.png",
            Sha256 = "FACEHASH"
        });

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"scene-image-native-reference-{Guid.NewGuid():N}.db");
        private readonly string _producedImagesDatabasePath = Path.Combine(Path.GetTempPath(), $"produced-images-native-reference-{Guid.NewGuid():N}.db");

        public Fixture()
        {
            Repository = new SceneImageRepository(Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={_databasePath}"
            }));
            ProducedImages = new ProducedImageRepository(Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={_producedImagesDatabasePath}"
            }));
            PromptOnlyClient = new RecordingPromptOnlyClient();
        }

        public SceneImageRepository Repository { get; }

        public ProducedImageRepository ProducedImages { get; }

        public RecordingPromptOnlyClient PromptOnlyClient { get; }

        public SceneImageRecord NewImage() => new()
        {
            Id = $"image-{Guid.NewGuid():N}",
            SessionId = "session-1",
            InteractionId = "interaction-1",
            PromptRecordId = "prompt-1",
            PromptSnapshot = "two people talking in a bedroom",
            Status = SceneImageStatus.Pending,
            RenderMode = SceneImageRenderMode.NativeReference,
            RequestedModelId = QwenImage21Model.Id,
            ImageSize = "1024x1024"
        };

        /// <summary>An identity-controlled render whose model carries identity natively (no configured mechanism).</summary>
        public SceneImageRecord NewIdentityImage()
        {
            var image = NewImage();
            image.RenderMode = SceneImageRenderMode.IdentityControlled;
            image.IdentityPacksJson = """[{"packId":"pack-1","characterLabel":"Becky"}]""";
            return image;
        }

        public BackgroundJobEnvelope JobFor(SceneImageRecord image) => new()
        {
            JobType = BackgroundJobTypes.SceneImageRendering,
            PayloadJson = JsonSerializer.Serialize(new SceneImageRenderingJobPayload
            {
                SessionId = image.SessionId,
                InteractionId = image.InteractionId,
                ImageRecordId = image.Id
            })
        };

        public SceneImageRenderingJobHandler CreateHandler(
            IReferenceConditionedImageClient referenceClient,
            IReferenceStrategyResolver? strategies = null,
            ISceneImageStorageService? storage = null)
        {
            return new SceneImageRenderingJobHandler(
                Repository,
                storage ?? new StubSceneImageStorage(),
                new StubModelResolution(),
                PromptOnlyClient,
                identityClient: null!,
                identityRequestCompiler: null!,
                poseClient: null!,
                poseResolver: null!,
                new SceneImagePromptCompilerRegistry([new StubCompiler()]),
                new NullDebugEventSink(),
                NullLogger<SceneImageRenderingJobHandler>.Instance,
                ProducedImages,
                referenceConditionedClient: referenceClient,
                referenceStrategyResolver: strategies);
        }

        /// <summary>
        /// The identity render fixture: the mechanism client and the mechanism request compiler both THROW, so
        /// reaching them from a native-capability model fails the test loudly instead of passing quietly.
        /// </summary>
        public SceneImageRenderingJobHandler CreateIdentityHandler(
            IReferenceStrategyResolver strategies,
            IReferenceConditionedImageClient referenceClient,
            byte[] faceBytes,
            CharacterImageIdentityPack pack,
            SceneImageReferenceAsset face,
            ISceneImageStorageService? storage = null)
        {
            return new SceneImageRenderingJobHandler(
                Repository,
                storage ?? new StubSceneImageStorage(),
                new StubModelResolution(),
                PromptOnlyClient,
                identityClient: new NeverCalledIdentityClient(),
                identityRequestCompiler: new NeverCalledIdentityRequestCompiler(),
                poseClient: null!,
                poseResolver: null!,
                new SceneImagePromptCompilerRegistry([new StubCompiler()]),
                new NullDebugEventSink(),
                NullLogger<SceneImageRenderingJobHandler>.Instance,
                ProducedImages,
                referenceConditionedClient: referenceClient,
                identityStorage: new StubIdentityStorage(faceBytes),
                referenceStrategyResolver: strategies,
                identityFaceResolver: new IdentityFaceReferenceResolver(new StubIdentityRepository(pack, face)));
        }

        public ValueTask DisposeAsync()
        {
            foreach (var path in new[] { _databasePath, _producedImagesDatabasePath })
            {
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                {
                    try
                    {
                        if (File.Exists(path + suffix))
                            File.Delete(path + suffix);
                    }
                    catch
                    {
                    }
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private static class QwenImage21Model
    {
        public const string Id = "8b2e4d16-3a5f-4c7e-9d10-5f6a7c8b9d20";

        public static ResolvedImageModel Resolve() => new(
            ProviderBaseUrl: "http://192.168.0.11:8188",
            ImageGenerationPath: string.Empty,
            ProviderTimeoutSeconds: 300,
            ApiKeyEncrypted: null,
            ModelIdentifier: "qwen_image_2.1_int8_convrot.safetensors",
            ContentPolicy: ImageContentPolicy.AdultAllowed,
            ProviderName: "Local ComfyUI (WOOD-GAME-MAIN 5080)",
            IsSessionOverride: false,
            SceneImageModelFamily: SceneImageModelFamily.QwenImage21,
            PromptDialect: SceneImagePromptDialect.NaturalLanguage,
            ImageProtocol: ImageProtocol.ComfyUi,
            ComfyUiUrl: "http://192.168.0.11:8188",
            QwenImage21: new QwenImage21Refs(
                UnetName: "qwen_image_2.1_int8_convrot.safetensors",
                TextEncoderName: "qwen3vl_8b_int8_convrot.safetensors",
                VaeName: "qwen_image_2.1_vae_bf16.safetensors",
                ResolutionBudget: 1024,
                MaxReferences: 16,
                Steps: 25,
                Cfg: 1.0,
                SamplerName: "euler",
                Scheduler: "simple"),
            RegisteredModelId: Id);
    }

    private sealed class StubModelResolution : IModelResolutionService
    {
        public Task<ResolvedImageModel> ResolveImageModelAsync(string? sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(QwenImage21Model.Resolve());

        public Task<ResolvedImageModel> ResolveImageModelByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => Task.FromResult(QwenImage21Model.Resolve());

        public Task<ResolvedModel> ResolveAsync(
            AppFunction function,
            string? sessionModelId = null,
            double? sessionTemperature = null,
            double? sessionTopP = null,
            int? sessionMaxTokens = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedModel> ResolveImagePromptModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneImageModelChoice>> ListSceneImageModelsAsync(bool identityCapableOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingPromptOnlyClient : IImageGenerationClient
    {
        public int Calls { get; private set; }

        public Task<byte[]?> GenerateAsync(
            ResolvedImageModel model,
            string prompt,
            string? size,
            string? negativePrompt = null,
            long? seed = null,
            CancellationToken cancellationToken = default,
            SceneImageGenerationOptions? options = null)
        {
            Calls++;
            return Task.FromResult<byte[]?>([1, 2, 3]);
        }

        public Task<(bool Success, string Message)> CheckImageModelHealthAsync(
            string baseUrl,
            string modelIdentifier,
            int timeoutSeconds,
            string? apiKeyEncrypted,
            string providerName,
            ImageContentPolicy contentPolicy,
            CancellationToken cancellationToken = default,
            ImageProtocol imageProtocol = ImageProtocol.OpenAiImages)
            => Task.FromResult((true, "ok"));
    }

    private sealed class RecordingReferenceClient : IReferenceConditionedImageClient
    {
        public int Calls { get; private set; }

        public List<ReferenceConditionedImageRequest> Requests { get; } = [];

        public Task<byte[]> GenerateWithReferencesAsync(
            ResolvedImageModel model,
            ReferenceConditionedImageRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Requests.Add(request);
            return Task.FromResult(new byte[] { 1, 2, 3 });
        }
    }

    private sealed class NullDebugEventSink : IRolePlayDebugEventSink
    {
        public Task WriteAsync(RolePlayDebugEventRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>The registry only has to hand back a compiler for the resolved family/dialect; the native-reference branch never compiles a prompt through it.</summary>
    private sealed class StubCompiler : ISceneImagePromptCompiler
    {
        public SceneImageModelFamily Family => SceneImageModelFamily.QwenImage21;

        public SceneImagePromptDialect PromptDialect => SceneImagePromptDialect.NaturalLanguage;

        public ISceneImageLLMPromptBuilder PromptBuilder => throw new NotSupportedException();

        public string CanonicalNegativePrompt => string.Empty;

        public string BuildNegativePrompt(SceneImageBeat beat, string pov) => string.Empty;
    }

    /// <summary>Storage for the success path: saves report a path, and reads serve the configured bytes (the pose skeleton).</summary>
    private sealed class StubSceneImageStorage(byte[]? bytes = null) : ISceneImageStorageService
    {
        public Task<string> SaveAsync(
            string sessionId, string fileName, Stream content, CancellationToken cancellationToken = default)
            => Task.FromResult($"{sessionId}/{fileName}");

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream(bytes ?? []));

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// The identity capability contract as the render asks it: this model carries identity by its own reference
    /// slots (native) or by a configured mechanism, and nothing else.
    /// </summary>
    private sealed class StubReferenceStrategies(bool native, bool mechanism = false, bool poseControlNet = false) : IReferenceStrategyResolver
    {
        public Task<ReferenceStrategyResolution> ResolveAsync(
            string modelId, string strategy, CancellationToken cancellationToken = default)
        {
            var available = strategy switch
            {
                ReferenceStrategyResolver.IdentityNativeMultiReference => native,
                ReferenceStrategyResolver.IdentityReferenceConditioning => mechanism,
                ReferenceStrategyResolver.PoseControlNet => poseControlNet,
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

    private sealed class NeverCalledIdentityClient : IIdentityConditionedImageClient
    {
        public Task<byte[]> GenerateAsync(
            ResolvedIdentityImageModel model, IdentityControlledImageRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "The configured-mechanism client must not be reached when the model carries identity natively.");
    }

    private sealed class NeverCalledIdentityRequestCompiler : IIdentityControlledRequestCompiler
    {
        public Task<CompiledIdentityRequest> CompileAsync(
            IdentityRequestCompilationInput input, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "The configured-mechanism request compiler must not be reached when the model carries identity natively.");
    }
}
