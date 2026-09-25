using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.ModelManager;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The test render must use the model the operator PICKED. This is a silent-wrong guard: resolving the configured
/// default instead still returns a valid image, so the mistake produced a successful-looking test render of a model
/// nobody selected. Hit for real on 2026-09-25 — <c>ResolveImageModelAsync</c> takes a SESSION override, so passing a
/// model id there resolved the default, whose provider was serverless, and the test refused with
/// "ComfyUiServerless cannot run reference-conditioned generation" while the selected model was a local ComfyUI
/// model that could. Had the default happened to be ComfyUi, the wrong model would have rendered happily.
/// </summary>
public sealed class PoseTestRenderServiceTests
{
    [Fact]
    public async Task Render_ResolvesThePinnedModel_NotTheConfiguredDefault()
    {
        var models = new StubModels();
        var poseClient = new RecordingPoseClient();
        var service = new PoseTestRenderService(
            models,
            new StubStrategies(),
            new StubPoseModels(),
            poseClient,
            NullLogger<PoseTestRenderService>.Instance);

        var result = await service.RenderAsync(new PoseTestRenderRequest(
            Skeleton: [1, 2, 3],
            PoseLabel: "a loaded pose",
            ModelId: "the-pinned-model",
            Prompt: "a person standing",
            NegativePrompt: null,
            Size: "1024x1024",
            Seed: 7L));

        Assert.Equal("the-pinned-model", models.ResolvedById);
        Assert.Equal(0, models.DefaultResolutionCalls);
        Assert.Equal("OpenPose ControlNet graph", result.Mechanism);

        // The skeleton and the operator's own values must reach the client, not a default.
        Assert.Equal([1, 2, 3], poseClient.LastRequest?.PoseImageBytes);
        Assert.Equal(7L, poseClient.LastRequest?.Seed);
        Assert.Equal("1024x1024", poseClient.LastRequest?.Size);
    }

    /// <summary>
    /// The list is ordered by CAPABILITY, and the label comes from the same resolution the render takes.
    /// Alphabetical order previously put the ControlNet model first and the reference-image model last, so the
    /// default selection tested the one mechanism known to re-pose awkward stances (2026-09-25).
    /// </summary>
    [Fact]
    public async Task ListModels_PutsTheReferenceRouteFirst_AndLabelsEachMechanism()
    {
        var service = new PoseTestRenderService(
            new StubModels(),
            new StubStrategies(),
            new StubPoseModels(),
            new RecordingPoseClient(),
            NullLogger<PoseTestRenderService>.Instance);

        var models = await service.ListModelsAsync();

        Assert.Equal(["qwen", "biglust", "nothing"], models.Select(choice => choice.ModelId));
        Assert.Equal("pose as a reference image", models[0].MechanismLabel);
        Assert.True(models[0].CanCarryPose);
        Assert.Equal("OpenPose ControlNet graph", models[1].MechanismLabel);
        Assert.True(models[1].CanCarryPose);

        // An unusable model is listed WITH its reason rather than dropped, so its absence is never the only
        // explanation the operator gets.
        Assert.False(models[2].CanCarryPose);
        Assert.False(string.IsNullOrWhiteSpace(models[2].Reason));
    }

    /// <summary>An empty skeleton is refused rather than rendered: a render without it looks like a pass.</summary>
    [Fact]
    public async Task Render_RefusesAnEmptySkeleton()
    {
        var service = new PoseTestRenderService(
            new StubModels(),
            new StubStrategies(),
            new StubPoseModels(),
            new RecordingPoseClient(),
            NullLogger<PoseTestRenderService>.Instance);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RenderAsync(new PoseTestRenderRequest(
                Skeleton: [], "a pose", "the-pinned-model", "a person", null, "1024x1024", null)));

        Assert.Contains("no pose to test", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubModels : IModelResolutionService
    {
        public string? ResolvedById { get; private set; }

        public int DefaultResolutionCalls { get; private set; }

        /// <summary>
        /// Throws. Reverting this service to the default-resolution call must FAIL here rather than quietly test
        /// whatever model the app happens to default to.
        /// </summary>
        public Task<ResolvedImageModel> ResolveImageModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
        {
            DefaultResolutionCalls++;
            throw new InvalidOperationException(
                "The pose test render resolved the configured default image model instead of the pinned one.");
        }

        public Task<ResolvedImageModel> ResolveImageModelByIdAsync(
            string modelId, CancellationToken cancellationToken = default)
        {
            ResolvedById = modelId;
            return Task.FromResult(Model());
        }

        public Task<ResolvedModel> ResolveAsync(
            AppFunction function,
            string? sessionModelId = null,
            double? sessionTemperature = null,
            double? sessionTopP = null,
            int? sessionMaxTokens = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedModel> ResolveImagePromptModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelByIdAsync(
            string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneImageModelChoice>> ListSceneImageModelsAsync(
            bool identityCapableOnly, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneImageModelChoice>>(
            [
                new SceneImageModelChoice("biglust", "BigLust v1.6", "bigLust_v16.safetensors", "Local ComfyUI", HasIdentity: true),
                new SceneImageModelChoice("qwen", "Qwen-Image-2.1", "qwen_image_2.1_int8_convrot.safetensors", "Local ComfyUI", HasIdentity: true),
                new SceneImageModelChoice("nothing", "An API model", "some-api-model", "Some API", HasIdentity: false)
            ]);

        private static ResolvedImageModel Model() => new(
            ProviderBaseUrl: "http://192.168.0.11:8188",
            ImageGenerationPath: string.Empty,
            ProviderTimeoutSeconds: 300,
            ApiKeyEncrypted: null,
            ModelIdentifier: "juggernautXL_ragnarok.safetensors",
            ContentPolicy: ImageContentPolicy.AdultAllowed,
            ProviderName: "Local ComfyUI",
            IsSessionOverride: false,
            SceneImageModelFamily: SceneImageModelFamily.Sdxl,
            PromptDialect: SceneImagePromptDialect.NaturalLanguage,
            ImageProtocol: ImageProtocol.ComfyUi);
    }

    /// <summary>
    /// Answers per model, so a model that carries a pose only through its own reference slots is distinguishable
    /// from one that carries it through ControlNet, and from one that cannot carry one at all.
    /// </summary>
    private sealed class StubStrategies : IReferenceStrategyResolver
    {
        public Task<ReferenceStrategyResolution> ResolveAsync(
            string modelId, string strategy, CancellationToken cancellationToken = default)
        {
            var possible = modelId switch
            {
                // Carries a pose ONLY through its own reference slots.
                "qwen" => strategy == ReferenceStrategyResolver.IdentityNativeMultiReference,
                // Carries nothing at all.
                "nothing" => false,
                // Every other model carries a pose through the configured ControlNet graph.
                _ => true
            };

            return Task.FromResult(possible
                ? new ReferenceStrategyResolution(
                    ReferenceStrategyResolutionStatus.Possible, strategy, $"{strategy} is available.")
                : new ReferenceStrategyResolution(
                    ReferenceStrategyResolutionStatus.Impossible, strategy, $"{strategy} is not qualified for '{modelId}'."));
        }
    }

    private sealed class StubPoseModels : IPoseImageModelResolver
    {
        public Task<ResolvedPoseImageModel> ResolveAsync(
            string modelId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ResolvedPoseImageModel(
                ProviderBaseUrl: "http://192.168.0.11:8188",
                ProviderTimeoutSeconds: 300,
                ModelIdentifier: "juggernautXL_ragnarok.safetensors",
                ContentPolicy: ImageContentPolicy.AdultAllowed,
                ProviderName: "Local ComfyUI",
                ControlNetAdapterRef: "thibaud-openpose-xl2\\OpenPoseXL2.safetensors",
                DefaultStrength: 0.8));
    }

    private sealed class RecordingPoseClient : IPoseConditionedImageClient
    {
        public PoseConditionedImageRequest? LastRequest { get; private set; }

        public Task<byte[]> GenerateAsync(
            ResolvedPoseImageModel model,
            PoseConditionedImageRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new byte[] { 9, 9, 9 });
        }
    }
}
