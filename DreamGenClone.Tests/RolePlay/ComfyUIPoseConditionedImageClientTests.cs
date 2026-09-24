using System.Net;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ComfyUIPoseConditionedImageClientTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _client = new HttpClient(handler);
        public HttpClient CreateClient(string name) => _client;
    }

    private static ResolvedPoseImageModel Resolve() => new(
        ProviderBaseUrl: "http://192.168.0.16:8188",
        ProviderTimeoutSeconds: 600,
        ModelIdentifier: "bigLust_v16.safetensors",
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        ProviderName: "Local ComfyUI (WOOD-GAME-MAIN 5080)",
        ControlNetAdapterRef: "thibaud-openpose-xl2\\OpenPoseXL2.safetensors",
        DefaultStrength: 0.8,
        ImageProtocol: ImageProtocol.ComfyUi);

    private static PoseConditionedImageRequest Request() => new()
    {
        PositivePrompt = "a photo of a woman, standing, fully clothed, studio background",
        NegativePrompt = "blurry, low quality",
        Size = "1024x1024",
        Seed = 42,
        PoseImageBytes = new byte[] { 137, 80, 78, 71, 1, 2, 3 },
        Strength = 0.8,
        CorrelationId = "pose-test-1"
    };

    private static ComfyUIPoseConditionedImageClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new FakeHttpClientFactory(new StubHttpMessageHandler(responder)), NullLogger<ComfyUIPoseConditionedImageClient>.Instance);

    [Fact]
    public void BuildOpenPoseSdxlWorkflow_HasExpectedControlNetGraph()
    {
        var wf = ComfyUIPoseConditionedImageClient.BuildOpenPoseSdxlWorkflow(
            "bigLust_v16.safetensors",
            "thibaud-openpose-xl2\\OpenPoseXL2.safetensors",
            "pose_abc.png",
            Request());

        // Checkpoint + pose image load.
        Assert.Equal("CheckpointLoaderSimple", (string?)wf["4"]!["class_type"]);
        Assert.Equal("bigLust_v16.safetensors", (string?)wf["4"]!["inputs"]!["ckpt_name"]);
        Assert.Equal("LoadImage", (string?)wf["1"]!["class_type"]);
        Assert.Equal("pose_abc.png", (string?)wf["1"]!["inputs"]!["image"]);

        // OpenPose ControlNet loader + apply chain.
        Assert.Equal("ControlNetLoader", (string?)wf["11"]!["class_type"]);
        Assert.Equal("thibaud-openpose-xl2\\OpenPoseXL2.safetensors", (string?)wf["11"]!["inputs"]!["control_net_name"]);
        Assert.Equal("ControlNetApplyAdvanced", (string?)wf["12"]!["class_type"]);
        Assert.Equal(0.8, (double)wf["12"]!["inputs"]!["strength"]);
        Assert.Equal(new JsonArray("1", 0).ToJsonString(), wf["12"]!["inputs"]!["image"]!.ToJsonString());
        Assert.Equal(0.0, (double)wf["12"]!["inputs"]!["start_percent"]);
        Assert.Equal(1.0, (double)wf["12"]!["inputs"]!["end_percent"]);

        // Sampler consumes the ControlNet-conditioned positive/negative.
        Assert.Equal("KSampler", (string?)wf["3"]!["class_type"]);
        Assert.Equal(new JsonArray("12", 0).ToJsonString(), wf["3"]!["inputs"]!["positive"]!.ToJsonString());
        Assert.Equal(new JsonArray("12", 1).ToJsonString(), wf["3"]!["inputs"]!["negative"]!.ToJsonString());
        Assert.Equal(42L, (long)wf["3"]!["inputs"]!["seed"]);
        Assert.Equal(30, (int)wf["3"]!["inputs"]!["steps"]);

        // Prompt nodes feed the CLIP, decode + save terminate the graph.
        Assert.Equal("CLIPTextEncode", (string?)wf["6"]!["class_type"]);
        Assert.Equal("CLIPTextEncode", (string?)wf["7"]!["class_type"]);
        Assert.Equal("VAEDecode", (string?)wf["8"]!["class_type"]);
        Assert.Equal("SaveImage", (string?)wf["9"]!["class_type"]);
        Assert.Equal(new JsonArray("8", 0).ToJsonString(), wf["9"]!["inputs"]!["images"]!.ToJsonString());
    }

    [Fact]
    public void BuildOpenPoseSdxlWorkflow_DefaultSize_Is1024x1024()
    {
        var wf = ComfyUIPoseConditionedImageClient.BuildOpenPoseSdxlWorkflow(
            "bigLust_v16.safetensors",
            "thibaud-openpose-xl2\\OpenPoseXL2.safetensors",
            "pose_abc.png",
            new PoseConditionedImageRequest { PoseImageBytes = [1], Strength = 0.8, CorrelationId = "x" });

        Assert.Equal(1024, (int)wf["5"]!["inputs"]!["width"]);
        Assert.Equal(1024, (int)wf["5"]!["inputs"]!["height"]);
    }

    /// <summary>The FLUX references a test supplies, all configured — nothing in the graph is an app default.</summary>
    private static ResolvedPoseImageModel ResolveFlux() => new(
        ProviderBaseUrl: "http://192.168.0.11:8188",
        ProviderTimeoutSeconds: 600,
        ModelIdentifier: "flux1-dev-fp8.safetensors",
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        ProviderName: "Local ComfyUI (WOOD-GAME-MAIN 5080)",
        ControlNetAdapterRef: "flux-openpose-controlnet-raulc0399.safetensors",
        DefaultStrength: 0.85,
        ImageProtocol: ImageProtocol.ComfyUi,
        Family: SceneImageModelFamily.Flux,
        Flux: new FluxPoseRefs(
            UnetName: "flux1-dev-fp8.safetensors",
            ClipName1: "t5xxl_fp8_e4m3fn.safetensors",
            ClipName2: "clip_l.safetensors",
            VaeName: "ae.safetensors",
            ControlNetModelName: "flux-dev-fp8",
            Guidance: 3.5,
            Steps: 28,
            TimestepToStartCfg: 1));

    /// <summary>
    /// The FLUX OpenPose graph is the XLabs one, mirroring the proven LOCAL workflow — UNETLoader + DualCLIPLoader +
    /// VAELoader with a plain CLIPTextEncode and FluxGuidance, conditioned through
    /// LoadFluxControlNet/ApplyFluxControlNet into XlabsSampler. It must NOT be the SDXL graph (a FLUX checkpoint
    /// through CheckpointLoaderSimple cannot run) and must NOT be the pod's variant, which used
    /// CheckpointLoaderSimple + CLIPTextEncodeFlux against the same file served as a checkpoint.
    /// </summary>
    [Fact]
    public void BuildOpenPoseFluxWorkflow_UsesTheXlabsGraph_NotTheSdxlOne()
    {
        var wf = ComfyUIPoseConditionedImageClient.BuildOpenPoseFluxWorkflow(ResolveFlux(), "pose_abc.png", Request());

        // FLUX loads a UNET plus its own encoders and VAE.
        Assert.Equal("UNETLoader", (string?)wf["4"]!["class_type"]);
        Assert.Equal("flux1-dev-fp8.safetensors", (string?)wf["4"]!["inputs"]!["unet_name"]);
        Assert.Equal("default", (string?)wf["4"]!["inputs"]!["weight_dtype"]);
        Assert.Equal("DualCLIPLoader", (string?)wf["2"]!["class_type"]);
        Assert.Equal("t5xxl_fp8_e4m3fn.safetensors", (string?)wf["2"]!["inputs"]!["clip_name1"]);
        Assert.Equal("clip_l.safetensors", (string?)wf["2"]!["inputs"]!["clip_name2"]);
        Assert.Equal("flux", (string?)wf["2"]!["inputs"]!["type"]);
        Assert.Equal("VAELoader", (string?)wf["11"]!["class_type"]);
        Assert.Equal("ae.safetensors", (string?)wf["11"]!["inputs"]!["vae_name"]);

        // The XLabs loader takes the ControlNet PATH and the FLUX variant it was built for — both configured.
        Assert.Equal("LoadFluxControlNet", (string?)wf["14"]!["class_type"]);
        Assert.Equal("flux-openpose-controlnet-raulc0399.safetensors", (string?)wf["14"]!["inputs"]!["controlnet_path"]);
        Assert.Equal("flux-dev-fp8", (string?)wf["14"]!["inputs"]!["model_name"]);
        Assert.Equal("ApplyFluxControlNet", (string?)wf["15"]!["class_type"]);
        Assert.Equal(new JsonArray("9", 0).ToJsonString(), wf["15"]!["inputs"]!["image"]!.ToJsonString());
        Assert.Equal(0.8, (double)wf["15"]!["inputs"]!["strength"]);
        Assert.Equal("LoadImage", (string?)wf["9"]!["class_type"]);
        Assert.Equal("pose_abc.png", (string?)wf["9"]!["inputs"]!["image"]);

        // The guidance is applied to the POSITIVE only, and the sampler consumes the conditioned pair.
        Assert.Equal("FluxGuidance", (string?)wf["8"]!["class_type"]);
        Assert.Equal(new JsonArray("6", 0).ToJsonString(), wf["8"]!["inputs"]!["conditioning"]!.ToJsonString());
        Assert.Equal(3.5, (double)wf["8"]!["inputs"]!["guidance"]);
        Assert.Equal("XlabsSampler", (string?)wf["3"]!["class_type"]);
        Assert.Equal(new JsonArray("8", 0).ToJsonString(), wf["3"]!["inputs"]!["conditioning"]!.ToJsonString());
        Assert.Equal(new JsonArray("7", 0).ToJsonString(), wf["3"]!["inputs"]!["neg_conditioning"]!.ToJsonString());
        Assert.Equal(new JsonArray("15", 0).ToJsonString(), wf["3"]!["inputs"]!["controlnet_condition"]!.ToJsonString());

        // Configured sampling values, and the two structural ones for a from-nothing render.
        Assert.Equal(28, (int)wf["3"]!["inputs"]!["steps"]);
        Assert.Equal(1, (int)wf["3"]!["inputs"]!["timestep_to_start_cfg"]);
        Assert.Equal(3.5, (double)wf["3"]!["inputs"]!["true_gs"]);
        Assert.Equal(0.0, (double)wf["3"]!["inputs"]!["image_to_image_strength"]);
        Assert.Equal(1.0, (double)wf["3"]!["inputs"]!["denoise_strength"]);
        Assert.Equal(42L, (long)wf["3"]!["inputs"]!["noise_seed"]);

        // No SDXL node types survive into the FLUX graph.
        foreach (var node in wf)
        {
            var type = (string?)node.Value!["class_type"];
            Assert.NotEqual("CheckpointLoaderSimple", type);
            Assert.NotEqual("ControlNetApplyAdvanced", type);
            Assert.NotEqual("KSampler", type);
        }
    }

    /// <summary>A FLUX model with no resolved references must fail before building a half-graph.</summary>
    [Fact]
    public void BuildOpenPoseFluxWorkflow_WithoutResolvedRefs_Throws()
    {
        var flux = ResolveFlux();
        var incomplete = flux with { Flux = null };

        var ex = Assert.Throws<ImageGenerationException>(
            () => ComfyUIPoseConditionedImageClient.BuildOpenPoseFluxWorkflow(incomplete, "pose_abc.png", Request()));

        Assert.Equal("pose_flux_refs_missing", ex.ReasonCode);
    }

    [Fact]
    public async Task GenerateAsync_StrengthOutOfRange_ThrowsBeforeHttp()
    {
        var client = BuildClient(_ => throw new InvalidOperationException("HTTP must not be called."));
        var request = Request();
        request.Strength = 1.5;

        var ex = await Assert.ThrowsAsync<ImageGenerationException>(
            () => client.GenerateAsync(Resolve(), request, CancellationToken.None));
        Assert.Equal("pose_strength_out_of_range", ex.ReasonCode);
    }

    [Fact]
    public async Task GenerateAsync_MissingPoseBytes_ThrowsBeforeHttp()
    {
        var client = BuildClient(_ => throw new InvalidOperationException("HTTP must not be called."));
        var request = Request();
        request.PoseImageBytes = [];

        var ex = await Assert.ThrowsAsync<ImageGenerationException>(
            () => client.GenerateAsync(Resolve(), request, CancellationToken.None));
        Assert.Equal("pose_image_missing", ex.ReasonCode);
    }

    [Fact]
    public async Task Dispatcher_HostedProtocol_FailsFast()
    {
        var dispatcher = new PoseConditionedImageClientDispatcher(
            BuildClient(_ => throw new InvalidOperationException("HTTP must not be called.")));
        var hostedModel = Resolve() with
        {
            ProviderName = "TogetherAI",
            ImageProtocol = ImageProtocol.OpenAiImages
        };

        var ex = await Assert.ThrowsAsync<ImageGenerationException>(
            () => dispatcher.GenerateAsync(hostedModel, Request(), CancellationToken.None));
        Assert.Equal("pose_requires_comfyui_protocol", ex.ReasonCode);
    }

    [Fact]
    public async Task GenerateAsync_Success_UploadsThenRenders()
    {
        var pngBytes = new byte[] { 137, 80, 78, 71, 9, 9, 9 };
        var promptId = "pose-123";
        JsonObject? submittedWorkflow = null;
        bool uploaded = false;

        var client = BuildClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/upload/image"))
            {
                uploaded = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonNode.Parse("{\"name\":\"pose_x.png\"}")!.ToJsonString())
                };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/prompt"))
            {
                var body = JsonNode.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!.AsObject();
                submittedWorkflow = body["prompt"]!.AsObject();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonNode.Parse($"{{\"prompt_id\":\"{promptId}\"}}")!.ToJsonString())
                };
            }
            if (req.RequestUri!.AbsolutePath.Contains("/history/"))
            {
                var hist = JsonNode.Parse(
                    $"{{\"{promptId}\":{{\"status\":{{\"status_str\":\"success\"}},\"outputs\":{{\"9\":{{\"images\":[{{\"filename\":\"out.png\",\"subfolder\":\"\",\"type\":\"output\"}}]}}}}}}}}");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(hist!.ToJsonString()) };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/view"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pngBytes) };
            }
            throw new InvalidOperationException($"Unexpected request: {req.RequestUri}");
        });

        var bytes = await client.GenerateAsync(Resolve(), Request(), CancellationToken.None);

        Assert.True(uploaded, "pose image should be uploaded first");
        Assert.NotNull(submittedWorkflow);
        Assert.Equal("bigLust_v16.safetensors", (string?)submittedWorkflow!["4"]!["inputs"]!["ckpt_name"]);
        Assert.Equal(bytes, pngBytes);
    }
}
