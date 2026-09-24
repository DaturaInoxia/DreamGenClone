using System.Net;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using DreamGenClone.Web.Application.ModelManager;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Pins the Qwen-Image-2.1 generated graph. The autogrow reference wiring is the fragile part: two
/// plausible shapes fail SILENTLY or late (flat <c>image_N</c> kwargs -> TypeError in execute();
/// a hand-built <c>images</c> dict -> ignored, every reference dropped, job still "succeeds"), so the
/// exact flat <c>images.image_N</c> sub-input ids are asserted here.
/// </summary>
public sealed class ComfyUIImageClientQwenImage21Tests
{
    private static ResolvedImageModel Resolve(bool withQualification = true) => new(
        ProviderBaseUrl: "http://192.168.0.11:8188",
        ImageGenerationPath: "/v1/images/generations",
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
        QwenImage21: withQualification
            ? new QwenImage21Refs(
                UnetName: "qwen_image_2.1_int8_convrot.safetensors",
                TextEncoderName: "qwen3vl_8b_int8_convrot.safetensors",
                VaeName: "qwen_image_2.1_vae_bf16.safetensors",
                ResolutionBudget: 1024,
                MaxReferences: 16,
                Steps: 25,
                Cfg: 1.0,
                SamplerName: "euler",
                Scheduler: "simple")
            : null);

    private static JsonObject EncoderInputs(JsonObject workflow) =>
        workflow["4"]!["inputs"]!.AsObject();

    /// <summary>The qualified 2.1 envelope used across these tests (official values: cfg 1, euler/simple).</summary>
    private static QwenImage21Refs QualifiedRefs() => new(
        UnetName: "qwen_image_2.1_int8_convrot.safetensors",
        TextEncoderName: "qwen3vl_8b_int8_convrot.safetensors",
        VaeName: "qwen_image_2.1_vae_bf16.safetensors",
        ResolutionBudget: 1024,
        MaxReferences: 16,
        Steps: 25,
        Cfg: 1.0,
        SamplerName: "euler",
        Scheduler: "simple");

    [Fact]
    public void BuildWorkflow_TextToImage_WiresSplitLoadersAndInertNegative()
    {
        var wf = ComfyUIImageClient.BuildQwenImage21Workflow(
            "qwen_image_2.1_int8_convrot.safetensors",
            QualifiedRefs(),
            prompt: "a woman reading by a window",
            negative: "",
            size: "1024x1024",
            seed: 20260922L);

        Assert.Equal("UNETLoader", wf["1"]!["class_type"]!.GetValue<string>());
        Assert.Equal("qwen_image_2.1_int8_convrot.safetensors", wf["1"]!["inputs"]!["unet_name"]!.GetValue<string>());

        Assert.Equal("CLIPLoader", wf["2"]!["class_type"]!.GetValue<string>());
        Assert.Equal("qwen_image", wf["2"]!["inputs"]!["type"]!.GetValue<string>());
        Assert.Equal("VAELoader", wf["3"]!["class_type"]!.GetValue<string>());
        Assert.Equal("qwen_image_2.1_vae_bf16.safetensors", wf["3"]!["inputs"]!["vae_name"]!.GetValue<string>());

        Assert.Equal("TextEncodeQwenImage21", wf["4"]!["class_type"]!.GetValue<string>());
        var encoder = EncoderInputs(wf);
        Assert.Equal("1024x1024", $"{wf["5"]!["inputs"]!["width"]!.GetValue<int>()}x{wf["5"]!["inputs"]!["height"]!.GetValue<int>()}");
        Assert.Equal(1024, encoder["resolution"]!.GetValue<int>());
        Assert.False(encoder.ContainsKey("vae"), "the published text-to-image template leaves the encoder VAE disconnected");
        Assert.DoesNotContain(encoder, pair => pair.Key.StartsWith("images", StringComparison.Ordinal));

        var sampler = wf["6"]!["inputs"]!.AsObject();
        Assert.Equal(1.0, sampler["cfg"]!.GetValue<double>());
        Assert.Equal("euler", sampler["sampler_name"]!.GetValue<string>());
        Assert.Equal("simple", sampler["scheduler"]!.GetValue<string>());
        Assert.Equal(1.0, sampler["denoise"]!.GetValue<double>());
        Assert.Equal(20260922L, sampler["seed"]!.GetValue<long>());
        Assert.Equal("4", sampler["positive"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal(1, sampler["negative"]!.AsArray()[1]!.GetValue<int>());
        Assert.Equal("5", sampler["latent_image"]!.AsArray()[0]!.GetValue<string>());

        Assert.DoesNotContain(wf, node => node.Value!["class_type"]!.GetValue<string>() == "LoadImage");
    }

    [Fact]
    public void BuildWorkflow_WithReferences_UsesFlatAutogrowSubInputIds()
    {
        var wf = ComfyUIImageClient.BuildQwenImage21Workflow(
            "qwen_image_2.1_int8_convrot.safetensors",
            QualifiedRefs(),
            prompt: "two people in the bedroom from <image1>",
            negative: "",
            size: "1216x832",
            seed: 1L,
            referenceImageNames: ["qwen21-bedroom.png", "qwen21-becky.png"]);

        var encoder = EncoderInputs(wf);
        Assert.Equal("3", encoder["vae"]!.AsArray()[0]!.GetValue<string>());

        // The ONLY shape ComfyUI accepts: flat dotted autogrow sub-input ids.
        Assert.Equal("20", encoder["images.image_1"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("21", encoder["images.image_2"]!.AsArray()[0]!.GetValue<string>());
        Assert.False(encoder.ContainsKey("image_1"), "flat image_N kwargs reach execute() as unexpected keywords");
        Assert.False(encoder.ContainsKey("images"), "a hand-built images dict matches no declared input and is silently ignored");

        Assert.Equal("LoadImage", wf["20"]!["class_type"]!.GetValue<string>());
        Assert.Equal("qwen21-bedroom.png", wf["20"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal("LoadImage", wf["21"]!["class_type"]!.GetValue<string>());
        Assert.Equal("qwen21-becky.png", wf["21"]!["inputs"]!["image"]!.GetValue<string>());

        // References must not hijack the canvas: generation still draws from EmptyLatentImage.
        Assert.Equal("5", wf["6"]!["inputs"]!["latent_image"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("EmptyLatentImage", wf["5"]!["class_type"]!.GetValue<string>());
    }

    /// <summary>
    /// The sampler envelope is a MODEL property on 2.1, taken from the graph builder's <see cref="QwenImage21Refs"/>
    /// (i.e. from the model's qualified Model Manager configuration) and never from the studio's sampler controls.
    /// Those controls hold SDXL-family values, and a 2.1 render that used them ran at cfg 5 / dpmpp_2m_sde / karras,
    /// which produced blown-out, grainy output (reported 2026-09-24). The builder therefore takes no options bag at
    /// all - the same posture as the FLUX builder - so a foreign family's recipe cannot reach this graph.
    /// </summary>
    [Fact]
    public void BuildWorkflow_UsesTheQualifiedEnvelope_AndCannotBeHandedForeignFamilySettings()
    {
        var refs = QualifiedRefs() with { Steps = 40, Cfg = 1.0, SamplerName = "euler", Scheduler = "beta", ResolutionBudget = 2048 };
        var wf = ComfyUIImageClient.BuildQwenImage21Workflow(
            "u.safetensors", refs, prompt: "x", negative: "", size: null, seed: 5L);

        var sampler = wf["6"]!["inputs"]!.AsObject();
        Assert.Equal(40, sampler["steps"]!.GetValue<int>());
        Assert.Equal(1.0, sampler["cfg"]!.GetValue<double>());
        Assert.Equal("euler", sampler["sampler_name"]!.GetValue<string>());
        Assert.Equal("beta", sampler["scheduler"]!.GetValue<string>());
        Assert.Equal(2048, EncoderInputs(wf)["resolution"]!.GetValue<int>());
        // ParseSize default when no size is supplied.
        Assert.Equal(1024, wf["5"]!["inputs"]!["width"]!.GetValue<int>());

        // Structural guard: no parameter of this builder accepts the SDXL-family options bag, so a studio sampler
        // snapshot cannot be applied to a 2.1 render by any caller, present or future.
        var builder = typeof(ComfyUIImageClient).GetMethod(
            "BuildQwenImage21Workflow",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.DoesNotContain(
            builder.GetParameters(),
            parameter => parameter.ParameterType == typeof(SceneImageGenerationOptions));
    }

    [Fact]
    public async Task GenerateAsync_QwenImage21WithoutQualification_FailsFastBeforeSubmission()
    {
        var factory = new RecordingHttpClientFactory();
        var client = new ComfyUIImageClient(
            factory,
            new FakeEncryption(),
            NullLogger<ComfyUIImageClient>.Instance);

        var exception = await Assert.ThrowsAsync<ImageGenerationException>(() =>
            client.GenerateAsync(Resolve(withQualification: false), "x", "1024x1024", null, 1L, CancellationToken.None));

        Assert.Equal("missing_qwen_image_21_qualification", exception.ReasonCode);
        // The guard must fire BEFORE any ComfyUI call - not after a failed submission.
        Assert.Empty(factory.Requests);
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public List<string> Requests { get; } = [];

        public HttpClient CreateClient(string name) => new(new RecordingHandler(Requests));

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly List<string> _requests;

            public RecordingHandler(List<string> requests) => _requests = requests;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _requests.Add(request.RequestUri!.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
        }
    }

    private sealed class FakeEncryption : IApiKeyEncryptionService
    {
        public string Encrypt(string plainTextApiKey) => "enc:" + plainTextApiKey;
        public string Decrypt(string encryptedApiKey) => encryptedApiKey.Replace("enc:", "", StringComparison.Ordinal);
    }

    /// <summary>
    /// Emits the REAL builder output so it can be submitted to the local ComfyUI host unchanged
    /// (helpers/local-comfyui-host/run-local-proof.ps1 -WorkflowPath &lt;file&gt;). Hermetic by default:
    /// nothing is written unless QWEN21_EMIT_GRAPH names a path.
    /// </summary>
    [Fact]
    public void BuildWorkflow_EmitGraphForHostProof_WhenRequested()
    {
        var path = Environment.GetEnvironmentVariable("QWEN21_EMIT_GRAPH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var wf = ComfyUIImageClient.BuildQwenImage21Workflow(
            "qwen_image_2.1_int8_convrot.safetensors",
            QualifiedRefs(),
            prompt: Environment.GetEnvironmentVariable("QWEN21_EMIT_PROMPT")
                    ?? "Full-body editorial photograph of a woman in a charcoal knit sweater standing beside a tall industrial window in a sunlit loft apartment, natural skin texture, photorealistic, shot on 85mm",
            negative: "",
            size: "1024x1024",
            seed: 20260922L,
            // Optional reference slots, so the emitted graph can carry the same reference set the app would send
            // (identity face + pose skeleton). Each name must be uploaded to the host first — see
            // helpers/local-comfyui-host/run-local-proof.ps1 -ImagesJsonPath.
            referenceImageNames: ParseEmittedReferences(Environment.GetEnvironmentVariable("QWEN21_EMIT_REFS")));

        File.WriteAllText(path, wf.ToJsonString());
    }

    private static string[] ParseEmittedReferences(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [Fact]
    public void ModelSettings_ReadsArtifactsFromQualification()
    {
        var model = new RegisteredModel
        {
            Id = "m1",
            DisplayName = "Qwen-Image-2.1 (Local ComfyUI)",
            SceneImageModelFamily = SceneImageModelFamily.QwenImage21,
            PromptDialect = SceneImagePromptDialect.NaturalLanguage,
            CapabilityQualificationsJson =
                """
                [{"Strategy":"NativeMultiReference","EndpointId":"p1","Qualified":true,"ProofId":"proof-1",
                  "UnetName":"qwen_image_2.1_int8_convrot.safetensors",
                  "TextEncoderName":"qwen3vl_8b_int8_convrot.safetensors",
                  "VaeName":"qwen_image_2.1_vae_bf16.safetensors","Resolution":1024,"MaxReferences":16,
                  "Steps":25,"Cfg":1.0,"SamplerName":"euler","Scheduler":"simple"}]
                """
        };

        var refs = QwenImage21ModelSettings.Resolve(model);

        Assert.Equal("qwen_image_2.1_int8_convrot.safetensors", refs.UnetName);
        Assert.Equal("qwen3vl_8b_int8_convrot.safetensors", refs.TextEncoderName);
        Assert.Equal("qwen_image_2.1_vae_bf16.safetensors", refs.VaeName);
        Assert.Equal(1024, refs.ResolutionBudget);
        Assert.Equal(16, refs.MaxReferences);
        // The sampler envelope is model configuration, so a render cannot inherit the studio's SDXL recipe.
        Assert.Equal(25, refs.Steps);
        Assert.Equal(1.0, refs.Cfg);
        Assert.Equal("euler", refs.SamplerName);
        Assert.Equal("simple", refs.Scheduler);
    }

    /// <summary>
    /// Every 2.1 envelope value is REQUIRED qualification data. An unset one fails fast naming the exact field,
    /// because the alternative is a silently inherited value: an SDXL cfg of 5 on this cfg-1-distilled model is what
    /// produced the blown-out render reported 2026-09-24.
    /// </summary>
    [Theory]
    [InlineData("\"Cfg\":1.0,\"SamplerName\":\"euler\",\"Scheduler\":\"simple\"", "Steps")]
    [InlineData("\"Steps\":25,\"SamplerName\":\"euler\",\"Scheduler\":\"simple\"", "Cfg")]
    [InlineData("\"Steps\":25,\"Cfg\":1.0,\"Scheduler\":\"simple\"", "SamplerName")]
    [InlineData("\"Steps\":25,\"Cfg\":1.0,\"SamplerName\":\"euler\"", "Scheduler")]
    public void ModelSettings_MissingSamplerEnvelopeValue_FailsFastNamingTheField(string envelope, string missing)
    {
        var qualifications =
            "[{\"Strategy\":\"NativeMultiReference\",\"EndpointId\":\"p1\",\"Qualified\":true,\"ProofId\":\"proof-1\","
            + "\"UnetName\":\"u.safetensors\",\"TextEncoderName\":\"c.safetensors\",\"VaeName\":\"v.safetensors\","
            + "\"Resolution\":1024,\"MaxReferences\":16," + envelope + "}]";

        var model = new RegisteredModel
        {
            Id = "m1",
            DisplayName = "Qwen-Image-2.1 (Local ComfyUI)",
            SceneImageModelFamily = SceneImageModelFamily.QwenImage21,
            PromptDialect = SceneImagePromptDialect.NaturalLanguage,
            CapabilityQualificationsJson = qualifications
        };

        var exception = Assert.Throws<ModelResolutionException>(() => QwenImage21ModelSettings.Resolve(model));
        Assert.Contains(missing, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSettings_MissingArtifact_FailsFastNamingTheField()
    {
        var model = new RegisteredModel
        {
            Id = "m1",
            DisplayName = "Qwen-Image-2.1 (Local ComfyUI)",
            SceneImageModelFamily = SceneImageModelFamily.QwenImage21,
            PromptDialect = SceneImagePromptDialect.NaturalLanguage,
            CapabilityQualificationsJson =
                """
                [{"Strategy":"NativeMultiReference","EndpointId":"p1","Qualified":true,"ProofId":"proof-1",
                  "UnetName":"qwen_image_2.1_int8_convrot.safetensors","Resolution":1024,"MaxReferences":16,
                  "Steps":25,"Cfg":1.0,"SamplerName":"euler","Scheduler":"simple"}]
                """
        };

        var exception = Assert.Throws<ModelResolutionException>(() => QwenImage21ModelSettings.Resolve(model));
        Assert.Contains("TextEncoderName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSettings_UnqualifiedEntry_FailsFast()
    {
        var model = new RegisteredModel
        {
            Id = "m1",
            DisplayName = "Qwen-Image-2.1 (Local ComfyUI)",
            SceneImageModelFamily = SceneImageModelFamily.QwenImage21,
            PromptDialect = SceneImagePromptDialect.NaturalLanguage,
            CapabilityQualificationsJson =
                """
                [{"Strategy":"NativeMultiReference","EndpointId":"p1","Qualified":false,"ProofId":"proof-1",
                  "UnetName":"u","TextEncoderName":"c","VaeName":"v","Resolution":1024,"MaxReferences":16}]
                """
        };

        var exception = Assert.Throws<ModelResolutionException>(() => QwenImage21ModelSettings.Resolve(model));
        Assert.Contains("no passing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSettings_ReferenceResolutionBudget_ReadsResolution()
    {
        var model = new RegisteredModel
        {
            Id = "m1",
            DisplayName = "Qwen-Image-2.1 Editor (Local ComfyUI)",
            CapabilityQualificationsJson =
                """
                [{"Strategy":"NativeMultiReference","EndpointId":"p1","Qualified":true,"ProofId":"proof-1","Resolution":1024}]
                """
        };

        Assert.Equal(1024, QwenImage21ModelSettings.ResolveReferenceResolutionBudget(model));
    }

    private static ResolvedImageEditorModel ResolveEditor(int? resolutionBudget = 1024) => new(
        ComfyUiUrl: "http://192.168.0.11:8188",
        ProviderTimeoutSeconds: 300,
        ApiKeyEncrypted: null,
        ModelIdentifier: "qwen_image_2.1_editor",
        ProviderName: "Local ComfyUI (WOOD-GAME-MAIN 5080)",
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        DiffusionModel: "qwen_image_2.1_int8_convrot.safetensors",
        TextEncoder: "qwen3vl_8b_int8_convrot.safetensors",
        Vae: "qwen_image_2.1_vae_bf16.safetensors",
        Steps: 25,
        Cfg: 1.0,
        Sampler: "euler",
        Scheduler: "simple",
        Denoise: 1.0,
        AuraFlowShift: 0.0,
        CfgNormStrength: 0.0,
        ImageProtocol: ImageProtocol.ComfyUi,
        RegisteredModelId: "8b2e4d16-3a5f-4c7e-9d10-5f6a7c8b9d20",
        GraphKind: ImageEditorGraphKind.QwenImage21Native,
        ResolutionBudget: resolutionBudget);

    [Fact]
    public void BuildEditWorkflow_SourceIsImageOneAndReferencesFollow()
    {
        var wf = ComfyUIImageEditingClient.BuildResolvedWorkflow(
            ResolveEditor(),
            sourceImageName: "scene.png",
            instruction: "replace the face with <image2>",
            referenceImageNames: ["ref-a.png", "ref-b.png"]);

        Assert.Equal("LoadImage", wf["1"]!["class_type"]!.GetValue<string>());
        Assert.Equal("scene.png", wf["1"]!["inputs"]!["image"]!.GetValue<string>());

        var encoder = wf["6"]!["inputs"]!.AsObject();
        Assert.Equal("TextEncodeQwenImage21", wf["6"]!["class_type"]!.GetValue<string>());
        Assert.Equal("1", encoder["images.image_1"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("20", encoder["images.image_2"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("21", encoder["images.image_3"]!.AsArray()[0]!.GetValue<string>());
        Assert.False(encoder.ContainsKey("image_1"));
        Assert.False(encoder.ContainsKey("images"));
        Assert.Equal(1024, encoder["resolution"]!.GetValue<int>());
        Assert.Equal("3", encoder["clip"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("4", encoder["vae"]!.AsArray()[0]!.GetValue<string>());

        Assert.Equal("ref-a.png", wf["20"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal("ref-b.png", wf["21"]!["inputs"]!["image"]!.GetValue<string>());

        // QwenImage21Cache is the sampler's model source, and the latent comes from the encoder.
        Assert.Equal("QwenImage21Cache", wf["5"]!["class_type"]!.GetValue<string>());
        Assert.Equal("2", wf["5"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());
        var sampler = wf["7"]!["inputs"]!.AsObject();
        Assert.Equal("5", sampler["model"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("6", sampler["latent_image"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal(2, sampler["latent_image"]!.AsArray()[1]!.GetValue<int>());
        Assert.Equal(25, sampler["steps"]!.GetValue<int>());
        Assert.Equal(1.0, sampler["cfg"]!.GetValue<double>());

        Assert.Equal("UNETLoader", wf["2"]!["class_type"]!.GetValue<string>());
        Assert.Equal("qwen_image_2.1_int8_convrot.safetensors", wf["2"]!["inputs"]!["unet_name"]!.GetValue<string>());
        Assert.Equal("qwen_image", wf["3"]!["inputs"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void BuildEditWorkflow_QwenImage21WithoutResolutionBudget_FailsFast()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ComfyUIImageEditingClient.BuildResolvedWorkflow(
                ResolveEditor(resolutionBudget: null),
                sourceImageName: "scene.png",
                instruction: "x"));

        Assert.Contains("resolution budget", exception.Message, StringComparison.Ordinal);
    }
}
