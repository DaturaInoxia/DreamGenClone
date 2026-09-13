using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using DreamGenClone.Web.Application.RolePlay.Editing;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The deterministic enhance core (B-121 Enhance step): an upscale followed by a scale back to the target
/// long edge. The arithmetic of that scale and the operation's own refusals are tested here, so a broken
/// target size or a silent fallback cannot reach a pod.
/// </summary>
public sealed class ImageEnhanceTests
{
    [Fact]
    public void TargetSize_Landscape_ScalesTheLongEdgeToTheTarget()
    {
        Assert.Equal((1024, 512), ImageResizeEngine.ComputeTargetSize(2000, 1000, 1024));
    }

    [Fact]
    public void TargetSize_Portrait_ScalesTheLongEdgeToTheTarget()
    {
        Assert.Equal((512, 1024), ImageResizeEngine.ComputeTargetSize(1000, 2000, 1024));
    }

    [Fact]
    public void TargetSize_AlreadyWithinTarget_IsUnchanged()
    {
        // Scaling up here would be a second, silent resample of an image that has just been upscaled.
        Assert.Equal((800, 600), ImageResizeEngine.ComputeTargetSize(800, 600, 1024));
    }

    [Fact]
    public void TargetSize_ExactlyTheTarget_IsUnchanged()
    {
        Assert.Equal((1024, 768), ImageResizeEngine.ComputeTargetSize(1024, 768, 1024));
    }

    [Fact]
    public void TargetSize_KeepsTheAspectRatio()
    {
        var (width, height) = ImageResizeEngine.ComputeTargetSize(1600, 900, 1024);

        Assert.Equal(1024, width);
        Assert.Equal(576, height);
        Assert.Equal(1600.0 / 900.0, (double)width / height, 2);
    }

    [Fact]
    public async Task ScaleToLongEdge_SourceSmallerThanTheTarget_ReturnsTheSameBytes()
    {
        var engine = new ImageResizeEngine();
        var source = await CreatePngAsync(64, 48);

        var result = await engine.ScaleToLongEdgeAsync(source, 1024);

        Assert.Same(source, result);
    }

    [Fact]
    public async Task ScaleToLongEdge_LargerSource_ProducesTheTargetSize()
    {
        var engine = new ImageResizeEngine();
        var source = await CreatePngAsync(400, 200);

        var result = await engine.ScaleToLongEdgeAsync(source, 100);

        using var image = Image.Load<Rgba32>(result);
        Assert.Equal(100, image.Width);
        Assert.Equal(50, image.Height);
    }

    [Fact]
    public void EnhanceOperation_WithoutUpscaler_FailsNamingTheKey()
    {
        var operation = new MediaEditEnhanceOperation("  ", 1024);

        var error = Assert.Throws<InvalidOperationException>(operation.Validate);

        Assert.Contains("UpscalerModelName", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnhanceOperation_WithAnUnusableTargetEdge_FailsNamingTheKey()
    {
        var operation = new MediaEditEnhanceOperation("4x-UltraSharp.pth", 0);

        var error = Assert.Throws<InvalidOperationException>(operation.Validate);

        Assert.Contains("TargetLongEdge", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ForEnhance_CarriesTheEnhanceParametersAndNothingElse()
    {
        var operation = MediaEditOperation.ForEnhance(new MediaEditEnhanceOperation("4x-UltraSharp.pth", 1024));

        Assert.Equal(MediaEditOperationKind.Enhance, operation.Kind);
        Assert.Null(operation.Crop);
        Assert.Equal("4x-UltraSharp.pth", operation.Enhance!.UpscalerModelName);
        Assert.Equal(1024, operation.Enhance.TargetLongEdge);
    }

    [Fact]
    public void Resolver_UnknownKind_FailsFast()
    {
        var resolver = new MediaEditOperationExecutorResolver(
            [new CropOperationExecutor(new ImageCropEngine())]);

        var error = Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve(MediaEditOperationKind.Enhance));

        Assert.Contains("Enhance", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Executor_NonComfyUiEndpoint_RefusesInsteadOfGuessing()
    {
        var executor = BuildExecutor(new StubUpscaleClient(), ImageProtocol.OpenAiImages);
        var plan = BuildPlan(new MediaEditEnhanceOperation("4x-UltraSharp.pth", 1024));

        await using var source = new MemoryStream(await CreatePngAsync(64, 48));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(plan, source));

        Assert.Contains("ComfyUI", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Executor_UpscalesThenScalesToTheTargetLongEdge()
    {
        var upscaled = await CreatePngAsync(400, 200);
        var client = new StubUpscaleClient(upscaled);
        var executor = BuildExecutor(client, ImageProtocol.ComfyUi);
        var plan = BuildPlan(new MediaEditEnhanceOperation("4x-UltraSharp.pth", 100));

        await using var source = new MemoryStream(await CreatePngAsync(50, 25));
        var output = await executor.ExecuteAsync(plan, source);

        Assert.Equal(MediaEditOperationKind.Enhance, output.Operation);
        Assert.Equal("4x-UltraSharp.pth", client.RequestedUpscaler);
        Assert.Null(output.ModelIdentifier); // an operation must not claim a model rendered it
        using var image = Image.Load<Rgba32>(output.Bytes);
        Assert.Equal(100, image.Width);
        Assert.Equal(50, image.Height);
    }

    [Fact]
    public void UpscaleWorkflow_IsLoadUpscaleSave()
    {
        var graph = ComfyUIImageUpscaleClient.BuildWorkflow("source.png", "4x-UltraSharp.pth");

        Assert.Equal("LoadImage", graph["1"]!["class_type"]!.GetValue<string>());
        Assert.Equal("source.png", graph["1"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal("UpscaleModelLoader", graph["2"]!["class_type"]!.GetValue<string>());
        Assert.Equal("4x-UltraSharp.pth", graph["2"]!["inputs"]!["model_name"]!.GetValue<string>());
        Assert.Equal("ImageUpscaleWithModel", graph["3"]!["class_type"]!.GetValue<string>());
        Assert.Equal("2", graph["3"]!["inputs"]!["upscale_model"]![0]!.GetValue<string>());
        Assert.Equal("1", graph["3"]!["inputs"]!["image"]![0]!.GetValue<string>());
        Assert.Equal("SaveImage", graph["4"]!["class_type"]!.GetValue<string>());
        Assert.Equal("3", graph["4"]!["inputs"]!["images"]![0]!.GetValue<string>());
    }

    private static EnhanceOperationExecutor BuildExecutor(IImageUpscaleClient client, ImageProtocol protocol)
        => new(
            new StubEditorResolver(protocol),
            client,
            new ImageResizeEngine(),
            NullLogger<EnhanceOperationExecutor>.Instance);

    private static MediaEditRunPlan BuildPlan(MediaEditEnhanceOperation enhance)
        => new(
            ImageId: "image-1",
            SourceImageId: "source-1",
            SourceOpenAsync: _ => Task.FromResult<Stream>(new MemoryStream()),
            SourceSha256: "sha",
            Operation: MediaEditOperation.ForEnhance(enhance));

    private static async Task<byte[]> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var buffer = new MemoryStream();
        await image.SaveAsPngAsync(buffer);
        return buffer.ToArray();
    }

    private sealed class StubUpscaleClient : IImageUpscaleClient
    {
        private readonly byte[] _result;

        public StubUpscaleClient(byte[]? result = null) => _result = result ?? [];

        public string? RequestedUpscaler { get; private set; }

        public Task<byte[]> UpscaleAsync(
            ResolvedImageEditorModel endpoint,
            string upscalerModelName,
            Stream sourceImage,
            string sourceFileName,
            CancellationToken cancellationToken = default)
        {
            RequestedUpscaler = upscalerModelName;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubEditorResolver : IImageEditorModelResolver
    {
        private readonly ResolvedImageEditorModel _model;

        public StubEditorResolver(ImageProtocol protocol)
            => _model = new ResolvedImageEditorModel(
                ComfyUiUrl: "http://192.168.0.16:8188",
                ProviderTimeoutSeconds: 120,
                ApiKeyEncrypted: null,
                ModelIdentifier: "Qwen-Rapid-AIO-NSFW-v23.safetensors",
                ProviderName: "Local ComfyUI",
                ContentPolicy: ImageContentPolicy.AdultAllowed,
                DiffusionModel: null,
                TextEncoder: null,
                Vae: null,
                Steps: 8,
                Cfg: 1.0,
                Sampler: "euler_ancestral",
                Scheduler: "beta",
                Denoise: 1.0,
                AuraFlowShift: 3.1,
                CfgNormStrength: 1.0,
                ImageProtocol: protocol,
                RegisteredModelId: "22222222-2222-2222-2222-222222222222",
                GraphKind: ImageEditorGraphKind.MergedCheckpoint);

        public Task<ResolvedImageEditorModel> ResolveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_model);

        public Task<ResolvedImageEditorModel> ResolveByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => Task.FromResult(_model);

        public Task<IReadOnlyList<SceneImageModelChoice>> ListImageEditorModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneImageModelChoice>>([]);
    }
}
