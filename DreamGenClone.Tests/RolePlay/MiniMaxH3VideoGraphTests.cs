using System.Net;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Pins the MiniMax H3 (Ref2VA) graph and client against the shape verified on the host in the B-150 sweep
/// (<c>helpers/h3-local-host/run-h3-ref2va-proof.py</c>).
///
/// <para>
/// Every failure guarded here is SILENT on the host: a graph that drops <c>VAEDecodeAudio</c> renders a mute
/// file, one that binds reference images without the video VAE quietly conditions on the text encoder only, and
/// one that numbers the reference slots differently from the prompt's <c>&lt;Picture i&gt;</c> tags conditions on
/// the wrong image - all three submit successfully.
/// </para>
/// </summary>
public sealed class MiniMaxH3VideoGraphTests
{
    private const string Dit = "minimax_h3_ref2va_pruned_w4a8_mixed.safetensors";
    private const string Te = "qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors";
    private const string VideoVae = "minimax_h3_video_vae_int8_convrot.safetensors";
    private const string AudioVae = "minimax_h3_audio_vae_fp32.safetensors";

    internal static MiniMaxH3FramePolicy FramePolicy() => new(
        MinFrames: 5,
        MaxFrames: 3600,
        FrameStep: 17,
        TrainedMinFrames: 124,
        TrainedMaxFrames: 362,
        WarnAboveFrames: 192,
        PresetShortFrames: 124,
        PresetLongFrames: 192);

    internal static MiniMaxH3Refs Refs(
        string videoVae = VideoVae,
        string audioVae = AudioVae,
        int maxReferenceImages = 9) => new(
        DitName: Dit,
        TextEncoderName: Te,
        TextEncoderDevice: "cpu",
        VideoVaeName: videoVae,
        AudioVaeName: audioVae,
        SamplerName: "euler",
        Scheduler: "beta",
        Steps: 40,
        Denoise: 1.0,
        Fps: 24,
        BitDepth: 8,
        DefaultWidth: 1344,
        DefaultHeight: 768,
        DefaultRefImageSize: "match",
        FramePolicy: FramePolicy(),
        MaxReferenceImages: maxReferenceImages,
        MaxReferenceVideos: 3,
        MaxReferenceAudios: 3,
        LoudnessTargetLufs: -16,
        RenderTimeoutSeconds: 7200,
        FfmpegPath: @"D:\src\DreamGenClone\.venv\Lib\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe",
        MaxContinuationChainLength: 3);

    private static JsonObject Build(
        MiniMaxH3Refs? refs = null,
        IReadOnlyList<string>? references = null,
        int width = 1344,
        int height = 768,
        int length = 124,
        int steps = 40,
        string refImageSize = "match",
        IReadOnlyList<ResolvedSceneLora>? loras = null,
        string? guideImageName = null,
        string? guideAudioName = null,
        int guideFrameIndex = 0)
        => MiniMaxH3VideoGraph.Build(
            refs ?? Refs(),
            "STYLE.\n[Shot 1] something happens.",
            references ?? [],
            width,
            height,
            length,
            steps,
            seed: 42L,
            refImageSize,
            outputPrefix: "video/test",
            loras,
            guideImageName,
            guideAudioName,
            guideFrameIndex);

    // ---- B-156 continuation guide ---------------------------------------------------------------------------
    // The seam is ONE node, verified against the live host: MiniMaxH3AddGuide consumes the reference node's
    // conditioning AND latent and returns conditioning, so the guider must read the GUIDE when one exists - and the
    // graph must not pretend a continuation is a plain render when the guide could not be built.

    [Fact]
    public void Build_EmitsNoGuideForAPlainRender()
    {
        var graph = Build();

        Assert.False(graph.ContainsKey("41"));
        Assert.False(graph.ContainsKey("40"));
        Assert.False(graph.ContainsKey("42"));

        // The guider still reads the reference node directly, exactly as before this feature existed.
        Assert.Equal("10", graph["14"]!["inputs"]!["conditioning"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Build_EmitsTheGuideAndRepointsTheGuider()
    {
        var graph = Build(guideImageName: "frame-abc12345.png", guideFrameIndex: 0);

        var guide = graph["41"]!;
        Assert.Equal("MiniMaxH3AddGuide", guide["class_type"]!.GetValue<string>());
        var inputs = guide["inputs"]!.AsObject();
        Assert.Equal("10", inputs["positive"]![0]!.GetValue<string>());
        Assert.Equal(0, inputs["positive"]![1]!.GetValue<int>());
        Assert.Equal("10", inputs["latent"]![0]!.GetValue<string>());
        Assert.Equal(1, inputs["latent"]![1]!.GetValue<int>());
        Assert.Equal(0, inputs["frame_idx"]!.GetValue<int>());
        Assert.Equal("3", inputs["vae"]![0]!.GetValue<string>());
        Assert.Equal("4", inputs["audio_vae"]![0]!.GetValue<string>());
        Assert.Equal("40", inputs["image"]![0]!.GetValue<string>());
        Assert.False(inputs.ContainsKey("audio"));

        Assert.Equal("frame-abc12345.png", graph["40"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal("41", graph["14"]!["inputs"]!["conditioning"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Build_EmitsTheCarriedAudioWhenTheGuideCarriesIt()
    {
        var graph = Build(guideImageName: "frame-abc12345.png", guideAudioName: "last-audio.wav");

        Assert.Equal("LoadAudio", graph["42"]!["class_type"]!.GetValue<string>());
        Assert.Equal("last-audio.wav", graph["42"]!["inputs"]!["audio"]!.GetValue<string>());
        Assert.Equal("42", graph["41"]!["inputs"]!["audio"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Build_RefusesCarriedAudioWithoutTheFrameItBelongsTo()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Build(guideAudioName: "last-audio.wav"));

        Assert.Contains("without also carrying its final frame", exception.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(124)]
    public void Build_RefusesAGuideFrameIndexOutsideTheClip(int frameIndex)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Build(guideImageName: "frame-abc12345.png", guideFrameIndex: frameIndex));

        Assert.Contains("outside this clip's", exception.Message);
    }

    [Fact]
    public void Build_IsDeterministicWithAGuide()
    {
        var first = Build(guideImageName: "frame-abc12345.png", guideAudioName: "last-audio.wav").ToJsonString();
        var second = Build(guideImageName: "frame-abc12345.png", guideAudioName: "last-audio.wav").ToJsonString();

        // Same record in, same graph bytes out - which also keeps the host's execution cache warm across a relaunch.
        Assert.Equal(first, second);
    }

    [Fact]
    public void Build_AlwaysDecodesAndMuxesAudio()
    {
        var graph = Build();
        var audioDecode = graph["17"]!;
        Assert.Equal("VAEDecodeAudio", audioDecode["class_type"]!.GetValue<string>());

        var createVideo = graph["18"]!;
        Assert.Equal("CreateVideo", createVideo["class_type"]!.GetValue<string>());
        var audioSource = createVideo["inputs"]!["audio"]!.AsArray();
        Assert.Equal("17", audioSource[0]!.GetValue<string>());
        Assert.Equal(0, audioSource[1]!.GetValue<int>());

        // The picture half must still be wired, from the video VAE on the same sampler output.
        var videoSource = createVideo["inputs"]!["images"]!.AsArray();
        Assert.Equal("16", videoSource[0]!.GetValue<string>());
        Assert.Equal("3", graph["16"]!["inputs"]!["vae"]!.AsArray()[0]!.GetValue<string>());

        var save = graph["19"]!;
        Assert.Equal("SaveVideo", save["class_type"]!.GetValue<string>());
        Assert.Equal("18", save["inputs"]!["video"]!.AsArray()[0]!.GetValue<string>());
    }

    [Fact]
    public void Build_BindsReferenceSlotsInOperatorOrder()
    {
        var graph = Build(references: ["first.png", "second.png", "third.png"]);

        var node = graph["10"]!;
        Assert.Equal("MiniMaxH3ReferenceToVideo", node["class_type"]!.GetValue<string>());

        for (var index = 0; index < 3; index++)
        {
            var slot = node["inputs"]![$"ref_images.ref_image_{index}"]!.AsArray();
            var expectedNode = (30 + index).ToString();
            Assert.Equal(expectedNode, slot[0]!.GetValue<string>());
            Assert.Equal(0, slot[1]!.GetValue<int>());
            Assert.Equal(
                new[] { "first.png", "second.png", "third.png" }[index],
                graph[expectedNode]!["inputs"]!["image"]!.GetValue<string>());
        }
    }

    [Fact]
    public void Build_WithNoReferences_EmitsNoLoadImageAndNoReferenceInputs()
    {
        var graph = Build();

        Assert.DoesNotContain(graph, node => node.Value?["class_type"]?.GetValue<string>() == "LoadImage");
        Assert.DoesNotContain(
            "ref_images.ref_image_0",
            graph["10"]!["inputs"]!.AsObject().Select(entry => entry.Key));
    }

    [Fact]
    public void Build_RefusesReferenceImagesWithoutTheVideoVae()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Build(refs: Refs(videoVae: ""), references: ["first.png"]));

        Assert.Contains("video VAE", exception.Message);
    }

    [Fact]
    public void Build_RefusesAMissingAudioVae()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Build(refs: Refs(audioVae: ""), references: ["first.png"]));

        Assert.Contains("AudioVaeName", exception.Message);
    }

    [Fact]
    public void Build_BindsBothVaeInputsOnTheReferenceNode()
    {
        var graph = Build(references: ["first.png"]);

        Assert.Equal("3", graph["10"]!["inputs"]!["vae"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("4", graph["10"]!["inputs"]!["audio_vae"]!.AsArray()[0]!.GetValue<string>());
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(22, true)]
    [InlineData(56, true)]
    [InlineData(124, true)]
    [InlineData(192, true)]
    [InlineData(362, true)]
    [InlineData(6, false)]
    [InlineData(100, false)]
    [InlineData(4, false)]
    [InlineData(3617, false)]
    public void Build_AcceptsOnlyLengthsTheNodeAccepts(int frames, bool accepted)
    {
        if (accepted)
        {
            var graph = Build(length: frames);
            Assert.Equal(frames, graph["10"]!["inputs"]!["length"]!.GetValue<int>());
            return;
        }

        var exception = Assert.Throws<InvalidOperationException>(() => Build(length: frames));
        Assert.Contains("not a value the node accepts", exception.Message);
    }

    [Fact]
    public void Build_WarnsRangeIsTrainedRangeWhileTheNodeAcceptsMore()
    {
        var policy = FramePolicy();

        Assert.True(policy.IsAccepted(56));
        Assert.False(policy.IsTrained(56));
        Assert.True(policy.IsTrained(124));
        Assert.True(policy.IsTrained(362));
        Assert.False(policy.IsTrained(363));
        Assert.Equal(124, policy.PresetShortFrames);
        Assert.Equal(192, policy.PresetLongFrames);
    }

    [Fact]
    public void Build_ChainsLorasInConfiguredOrderAndGuiderReadsTheChainTail()
    {
        var graph = Build(loras:
        [
            new ResolvedSceneLora("sexytime.safetensors", 0.8, "sexytime"),
            new ResolvedSceneLora("realism.safetensors", 1.0, "realism"),
            new ResolvedSceneLora("facial.safetensors", 0.6, "facial")
        ]);

        // The chain hangs off the UNETLoader and each loader reads the previous one.
        Assert.Equal("1", graph["100"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("100", graph["101"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("101", graph["102"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());

        Assert.Equal("sexytime.safetensors", graph["100"]!["inputs"]!["lora_name"]!.GetValue<string>());
        Assert.Equal(0.8, graph["100"]!["inputs"]!["strength_model"]!.GetValue<double>());
        Assert.Equal("realism.safetensors", graph["101"]!["inputs"]!["lora_name"]!.GetValue<string>());
        Assert.Equal("facial.safetensors", graph["102"]!["inputs"]!["lora_name"]!.GetValue<string>());

        // The scheduler and the guider both read the tail, so the render and its sigmas use the LoRA'd model.
        Assert.Equal("102", graph["13"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("102", graph["14"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());
    }

    [Fact]
    public void Build_WithoutLoras_ReadsTheBaseModelDirectly()
    {
        var graph = Build();

        Assert.DoesNotContain(graph, node => node.Value?["class_type"]?.GetValue<string>() == "LoraLoaderModelOnly");
        Assert.Equal("1", graph["13"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("1", graph["14"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());
    }

    [Fact]
    public void Build_RefusesACanvasThatIsNotAMultipleOfThirtyTwo()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Build(width: 1343));
        Assert.Contains("multiple of 32", exception.Message);
    }

    [Fact]
    public void Build_RefusesMoreReferencesThanTheNodeAccepts()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Build(refs: Refs(maxReferenceImages: 2), references: ["a.png", "b.png", "c.png"]));

        Assert.Contains("at most 2 reference images", exception.Message);
    }

    [Fact]
    public void AcceptedFrameLengths_StepsFromTheMinimum()
    {
        var policy = FramePolicy();
        var lengths = MiniMaxH3VideoGraph.AcceptedFrameLengths(policy);

        Assert.Equal(5, lengths[0]);
        Assert.Equal(22, lengths[1]);
        // The node's max is 3600, but only values congruent to min (mod step) are legal: 3592 = 5 + 211 * 17.
        Assert.Equal(3592, lengths[^1]);
        Assert.All(lengths, length => Assert.True(policy.IsAccepted(length)));
        Assert.Contains(124, lengths);
        Assert.Contains(192, lengths);
        Assert.Contains(362, lengths);
    }

    [Fact]
    public void Build_RefusesAnOutOfRangeLoraStrength()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Build(loras: [new ResolvedSceneLora("x.safetensors", 0)]));

        Assert.Contains("not a positive number", exception.Message);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Client transport
    // ---------------------------------------------------------------------------------------------------------

    private sealed class FakeEncryption : IApiKeyEncryptionService
    {
        public string Encrypt(string plainTextApiKey) => "enc:" + plainTextApiKey;
        public string Decrypt(string encryptedApiKey) => encryptedApiKey.Replace("enc:", "");
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    internal static ResolvedVideoModel VideoModel() => new(
        ProviderBaseUrl: "https://comfy.kenacwood.net",
        ProviderTimeoutSeconds: 30,
        ApiKeyEncrypted: null,
        ModelIdentifier: Dit,
        ProviderName: "Local ComfyUI (WOOD-GAME-MAIN 5080)",
        Family: SceneImageModelFamily.MiniMaxH3Ref2VA,
        PromptDialect: SceneImagePromptDialect.MiniMaxH3SixSection,
        ImageProtocol: ImageProtocol.ComfyUi,
        H3: Refs());

    [Fact]
    public async Task GenerateAsync_UploadsReferencesSubmitsTheH3GraphAndReturnsTheClip()
    {
        var clip = new byte[] { 0, 0, 0, 24, 102, 116, 121, 112 };
        var promptId = "h3-prompt";
        JsonObject? submitted = null;

        var client = new MiniMaxH3VideoClient(
            new FakeHttpClientFactory(new StubHttpMessageHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/upload/image"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"name\":\"ref_0.png\",\"subfolder\":\"\"}")
                    };
                }
                if (path.EndsWith("/prompt"))
                {
                    submitted = JsonNode.Parse(request.Content!.ReadAsStringAsync().Result)!["prompt"]!.AsObject();
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent($"{{\"prompt_id\":\"{promptId}\"}}")
                    };
                }
                if (path.Contains($"/history/{promptId}"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent($$"""
                        {
                          "{{promptId}}": {
                            "status": { "status_str": "success" },
                            "outputs": { "19": { "video": [ { "filename": "h3_00001.mp4", "subfolder": "video/test", "type": "output" } ] } }
                          }
                        }
                        """)
                    };
                }
                if (path.EndsWith("/view"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(clip) };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            })),
            new FakeEncryption(),
            NullLogger<MiniMaxH3VideoClient>.Instance);

        var result = await client.GenerateAsync(
            VideoModel(),
            new SceneVideoGenerationRequest(
                Prompt: "compiled prompt",
                References: [new SceneVideoReferenceImage([1, 2, 3], "ref_0.png")],
                Width: 1344,
                Height: 768,
                Length: 124,
                Steps: 40,
                Seed: 7,
                RefImageSize: "match",
                OutputPrefix: "video/test",
                Loras: [new ResolvedSceneLora("sexytime.safetensors", 0.8)]));

        Assert.Equal(clip, result.VideoBytes);
        Assert.Equal("h3_00001.mp4", result.OutputFileName);

        Assert.NotNull(submitted);
        Assert.Equal("MiniMaxH3ReferenceToVideo", submitted!["10"]!["class_type"]!.GetValue<string>());
        // The uploaded name (not the local file name) is what the LoadImage node carries.
        Assert.Equal("ref_0.png", submitted["30"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal("LoraLoaderModelOnly", submitted["100"]!["class_type"]!.GetValue<string>());
        Assert.NotNull(submitted["17"]);
    }

    [Fact]
    public async Task GenerateAsync_RefusesAnIncompatibleFamilyDialect()
    {
        var model = VideoModel() with { PromptDialect = SceneImagePromptDialect.NaturalLanguage };

        var client = new MiniMaxH3VideoClient(
            new FakeHttpClientFactory(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            new FakeEncryption(),
            NullLogger<MiniMaxH3VideoClient>.Instance);

        var exception = await Assert.ThrowsAsync<ImageGenerationException>(() =>
            client.GenerateAsync(
                model,
                new SceneVideoGenerationRequest("p", [], 1344, 768, 124, 40, 1, "match", "video/test")));

        Assert.Equal("invalid_video_prompt_metadata", exception.ReasonCode);
    }
}
