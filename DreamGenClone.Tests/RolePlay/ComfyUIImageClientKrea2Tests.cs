using System.Net;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Pins the Krea 2 (Krea-2 Turbo) generated graph against the shape verified on the host
/// (<c>helpers/local-comfyui-host/run-krea2-proof.ps1</c>, 59 cells, 2026-10-01).
///
/// <para>
/// The failure this guards is SILENT: a graph that loads the wrong loader class, re-wires a clip input Krea 2 does
/// not have, or (worst) reintroduces a negative prompt still submits successfully and renders - just not the render
/// that was asked for, and not the one the family was qualified for.
/// </para>
/// </summary>
public sealed class ComfyUIImageClientKrea2Tests
{
    private const string Unet = "krea2_turbo_fp8_scaled.safetensors";
    private const string Clip = "qwen3vl_4b_fp8_scaled.safetensors";
    private const string Vae = "qwen_image_vae.safetensors";

    private static Krea2Refs QualifiedRefs() => new(
        UnetName: Unet,
        ClipName: Clip,
        VaeName: Vae,
        Steps: 8,
        Cfg: 1.0,
        SamplerName: "euler",
        Scheduler: "simple",
        Denoise: 1.0);

    private static ResolvedImageModel Model(
        IReadOnlyList<ResolvedSceneLora>? sceneLoras = null,
        IReadOnlyList<ResolvedCharacterLora>? characterLoras = null,
        bool withQualification = true) => new(
        ProviderBaseUrl: "http://192.168.0.11:8188",
        ImageGenerationPath: "/prompt",
        ProviderTimeoutSeconds: 300,
        ApiKeyEncrypted: null,
        ModelIdentifier: Unet,
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        ProviderName: "Local ComfyUI (WOOD-GAME-MAIN 5080)",
        IsSessionOverride: false,
        SceneImageModelFamily: SceneImageModelFamily.Krea2,
        PromptDialect: SceneImagePromptDialect.Krea2NaturalLanguage,
        ImageProtocol: ImageProtocol.ComfyUi,
        ComfyUiUrl: "http://192.168.0.11:8188",
        Krea2: withQualification ? QualifiedRefs() : null,
        Loras: characterLoras,
        SceneLoras: sceneLoras);

    [Fact]
    public void BuildWorkflow_WiresSplitLoadersAndTheQualifiedEnvelope()
    {
        var wf = ComfyUIImageClient.BuildKrea2Workflow(
            Unet,
            QualifiedRefs(),
            prompt: "a photorealistic editorial photograph of a woman reading by a window, 35mm, shallow depth of field",
            size: "1024x1024",
            seed: 20261001L);

        Assert.Equal("UNETLoader", wf["1"]!["class_type"]!.GetValue<string>());
        Assert.Equal(Unet, wf["1"]!["inputs"]!["unet_name"]!.GetValue<string>());
        Assert.Equal("default", wf["1"]!["inputs"]!["weight_dtype"]!.GetValue<string>());

        Assert.Equal("CLIPLoader", wf["2"]!["class_type"]!.GetValue<string>());
        Assert.Equal(Clip, wf["2"]!["inputs"]!["clip_name"]!.GetValue<string>());
        Assert.Equal("krea2", wf["2"]!["inputs"]!["type"]!.GetValue<string>());

        Assert.Equal("VAELoader", wf["3"]!["class_type"]!.GetValue<string>());
        Assert.Equal(Vae, wf["3"]!["inputs"]!["vae_name"]!.GetValue<string>());

        Assert.Equal("CLIPTextEncode", wf["4"]!["class_type"]!.GetValue<string>());
        Assert.Equal("2", wf["4"]!["inputs"]!["clip"]!.AsArray()[0]!.GetValue<string>());

        Assert.Equal("EmptyLatentImage", wf["6"]!["class_type"]!.GetValue<string>());
        Assert.Equal(1024, wf["6"]!["inputs"]!["width"]!.GetValue<int>());
        Assert.Equal(1024, wf["6"]!["inputs"]!["height"]!.GetValue<int>());

        var sampler = wf["7"]!["inputs"]!.AsObject();
        Assert.Equal(8, sampler["steps"]!.GetValue<int>());
        Assert.Equal(1.0, sampler["cfg"]!.GetValue<double>());
        Assert.Equal("euler", sampler["sampler_name"]!.GetValue<string>());
        Assert.Equal("simple", sampler["scheduler"]!.GetValue<string>());
        Assert.Equal(1.0, sampler["denoise"]!.GetValue<double>());
        Assert.Equal(20261001L, sampler["seed"]!.GetValue<long>());
        Assert.Equal("1", sampler["model"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("4", sampler["positive"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("6", sampler["latent_image"]!.AsArray()[0]!.GetValue<string>());

        Assert.Equal("VAEDecode", wf["8"]!["class_type"]!.GetValue<string>());
        Assert.Equal("SaveImage", wf["9"]!["class_type"]!.GetValue<string>());
    }

    /// <summary>
    /// Krea-2 Turbo takes NO negative text: the sampler's negative is a <c>ConditioningZeroOut</c> of the positive.
    /// The graph builder does not even accept a negative argument, so this asserts the shape as well as the absence -
    /// there must be exactly ONE text encode, and the zero-out must consume it.
    /// </summary>
    [Fact]
    public void BuildWorkflow_NegativeIsZeroOutOfPositive_NotASecondTextEncode()
    {
        var wf = ComfyUIImageClient.BuildKrea2Workflow(
            Unet, QualifiedRefs(), prompt: "a photograph", size: "1024x1024", seed: 1L);

        Assert.Equal("ConditioningZeroOut", wf["5"]!["class_type"]!.GetValue<string>());
        Assert.Equal("4", wf["5"]!["inputs"]!["conditioning"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("5", wf["7"]!["inputs"]!["negative"]!.AsArray()[0]!.GetValue<string>());

        var textEncodes = wf
            .Where(node => node.Value!["class_type"]!.GetValue<string>() == "CLIPTextEncode")
            .ToList();
        Assert.Single(textEncodes);
    }

    private static async Task<JsonNode> SubmitAsync(ResolvedImageModel model)
    {
        const string promptId = "prompt-krea2";
        HttpRequestMessage? submit = null;

        var client = new ComfyUIImageClient(
            new FakeHttpClientFactory(new StubHttpMessageHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/prompt", StringComparison.Ordinal))
                {
                    submit = request;
                    return Ok($"{{\"prompt_id\":\"{promptId}\"}}");
                }
                if (path.Contains($"/history/{promptId}", StringComparison.Ordinal))
                {
                    return Ok($"{{\"{promptId}\":{{\"status\":{{\"status_str\":\"success\"}},"
                        + "\"outputs\":{\"9\":{\"images\":[{\"filename\":\"out.png\",\"subfolder\":\"\",\"type\":\"output\"}]}}}}");
                }
                if (path.EndsWith("/view", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            })),
            new FakeEncryption(),
            NullLogger<ComfyUIImageClient>.Instance);

        await client.GenerateAsync(model, "a photograph", "1024x1024", null, seed: 42L);

        Assert.NotNull(submit);
        var body = await submit!.Content!.ReadAsStringAsync();
        return JsonNode.Parse(body)!["prompt"]!;
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    [Fact]
    public async Task GenerateAsync_WithNoLoras_SubmitsNoLoaderNodeAtAll()
    {
        var graph = await SubmitAsync(Model());

        Assert.DoesNotContain("LoraLoader", graph.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The chain order is the invariant that keeps identity intact: scene LoRAs (unlock / act) come FIRST and the
    /// character LoRA LAST, so the identity LoRA is applied closest to the prompt. Krea 2 uses
    /// <c>LoraLoaderModelOnly</c> - it has no separate clip conditioning path to re-wire - and the sampler alone is
    /// the model consumer.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_SceneThenCharacterLoras_ChainsModelOnlyLoadersInOrder()
    {
        var graph = await SubmitAsync(Model(
            sceneLoras:
            [
                new ResolvedSceneLora("krea2_nsfw_v4_v43exp.safetensors", 1.0, "Krea2 NSFW V4"),
                new ResolvedSceneLora("krea2_act_cowgirl_lokr.safetensors", 1.0, "Cowgirl act LoKr")
            ],
            characterLoras:
            [
                new ResolvedCharacterLora(
                    ArtifactId: "artifact-becky",
                    CharacterProfileId: "profile-becky",
                    CharacterName: "Becky",
                    TriggerToken: "ohwx-becky",
                    FileName: "dgc_lora_becky.safetensors",
                    Strength: 0.8,
                    Sha256: "AA")
            ]));

        Assert.Equal("LoraLoaderModelOnly", graph["20"]!["class_type"]!.GetValue<string>());
        Assert.Equal("krea2_nsfw_v4_v43exp.safetensors", graph["20"]!["inputs"]!["lora_name"]!.GetValue<string>());
        Assert.Equal("1", graph["20"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());
        Assert.False(
            graph["20"]!["inputs"]!.AsObject().ContainsKey("clip"),
            "Krea 2 has no clip input on its LoRA chain");

        Assert.Equal("krea2_act_cowgirl_lokr.safetensors", graph["21"]!["inputs"]!["lora_name"]!.GetValue<string>());
        Assert.Equal("20", graph["21"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());

        Assert.Equal("dgc_lora_becky.safetensors", graph["22"]!["inputs"]!["lora_name"]!.GetValue<string>());
        Assert.Equal(0.8, graph["22"]!["inputs"]!["strength_model"]!.GetValue<double>());
        Assert.Equal("21", graph["22"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());

        // The sampler samples the last node in the chain...
        Assert.Equal("22", graph["7"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());
        // ...and the text encode still reads the CLIPLoader, because the chain never touched the clip.
        Assert.Equal("2", graph["4"]!["inputs"]!["clip"]!.AsArray()[0]!.GetValue<string>());
    }

    [Fact]
    public async Task GenerateAsync_MissingQualification_FailsFast()
    {
        var exception = await Assert.ThrowsAsync<ImageGenerationException>(() =>
            SubmitAsync(Model(withQualification: false)));

        Assert.Contains("Krea 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("TextToImage", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FakeEncryption : IApiKeyEncryptionService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
