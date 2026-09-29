using System.Net;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The character-LoRA identity path in the ComfyUI graph, asserted STRUCTURALLY against the workflow the app
/// actually submits.
///
/// These are not shape tests. The failure this guards against is silent: a LoRA wired to the model branch alone
/// loads successfully, renders, and produces a different person than the one selected — with nothing in the log
/// to say the CLIP was left behind. So every test here checks the WIRING (that every consumer of model and clip
/// was re-pointed), not merely that a LoraLoader node exists.
/// </summary>
public sealed class SceneImageCharacterLoraGraphTests
{
    private static readonly ResolvedCharacterLora Becky = new(
        ArtifactId: "artifact-becky",
        CharacterProfileId: "profile-becky",
        CharacterName: "Becky",
        TriggerToken: "ohwx-becky",
        FileName: "dgc_lora_becky.safetensors",
        Strength: 0.8,
        Sha256: "AA");

    private static readonly ResolvedCharacterLora Dean = Becky with
    {
        ArtifactId = "artifact-dean",
        CharacterProfileId = "profile-dean",
        CharacterName = "Dean",
        TriggerToken = "ohwx-dean",
        FileName = "dgc_lora_dean.safetensors",
        Strength = 0.65
    };

    private static ResolvedImageModel Model(
        SceneImageModelFamily family,
        IReadOnlyList<ResolvedCharacterLora>? loras = null)
    {
        var (checkpoint, dialect) = family switch
        {
            SceneImageModelFamily.Sdxl => ("bigLust_v16.safetensors", SceneImagePromptDialect.SdxlNaturalLanguage),
            SceneImageModelFamily.Flux => ("flux1-dev.safetensors", SceneImagePromptDialect.FluxNaturalLanguage),
            _ => ("ponyDiffusionV6XL_v6.safetensors", SceneImagePromptDialect.PonyV6Tags)
        };

        return new ResolvedImageModel(
            ProviderBaseUrl: "https://host.test",
            ImageGenerationPath: "/prompt",
            ProviderTimeoutSeconds: 30,
            ApiKeyEncrypted: "enc:sekret",
            ModelIdentifier: checkpoint,
            ContentPolicy: ImageContentPolicy.AdultAllowed,
            ProviderName: "Local ComfyUI",
            IsSessionOverride: false,
            SceneImageModelFamily: family,
            PromptDialect: dialect,
            ImageProtocol: ImageProtocol.ComfyUi,
            ComfyUiUrl: "https://host.test",
            Loras: loras);
    }

    /// <summary>Submits a render and returns the workflow node graph that was actually POSTed to ComfyUI.</summary>
    private static async Task<JsonNode> SubmitAsync(
        ResolvedImageModel model,
        SceneImageGenerationOptions? options = null)
    {
        const string promptId = "prompt-1";
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

        await client.GenerateAsync(model, "a photograph", "1024x1024", null, seed: 42L, CancellationToken.None, options);

        Assert.NotNull(submit);
        var body = await submit!.Content!.ReadAsStringAsync();
        return JsonNode.Parse(body)!["prompt"]!;
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    [Fact]
    public async Task GenerateAsync_WithNoLoras_SubmitsNoLoraLoaderAtAll()
    {
        // The regression guard for every existing render: no selection must be byte-for-byte the graph the app
        // produced before this feature existed, so the reference/IP-Adapter route is provably untouched.
        var graph = await SubmitAsync(Model(SceneImageModelFamily.Sdxl));

        Assert.DoesNotContain(
            "LoraLoader",
            graph.ToJsonString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_PonyLora_RewiresTheSamplerAndTheClipSkipNodeThatFeedsBothEncodes()
    {
        var graph = await SubmitAsync(Model(SceneImageModelFamily.Pony, [Becky]));

        var lora = graph["20"]!;
        Assert.Equal("LoraLoader", lora["class_type"]!.GetValue<string>());
        Assert.Equal("dgc_lora_becky.safetensors", lora["inputs"]!["lora_name"]!.GetValue<string>());
        Assert.Equal(0.8, lora["inputs"]!["strength_model"]!.GetValue<double>());
        Assert.Equal(0.8, lora["inputs"]!["strength_clip"]!.GetValue<double>());
        // The chain starts at the checkpoint, not at nothing.
        Assert.Equal("4", lora["inputs"]!["model"]![0]!.GetValue<string>());

        // Pony's clip-skip node (10) feeds BOTH text encodes, so it is the one clip consumer to re-point...
        Assert.Equal("20", graph["10"]!["inputs"]!["clip"]![0]!.GetValue<string>());
        // ...and the sampler takes the LoRA'd model.
        Assert.Equal("20", graph["3"]!["inputs"]!["model"]![0]!.GetValue<string>());
        // The encodes themselves keep reading the clip-skip node, so they inherit the LoRA through it.
        Assert.Equal("10", graph["6"]!["inputs"]!["clip"]![0]!.GetValue<string>());
        Assert.Equal("10", graph["7"]!["inputs"]!["clip"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task GenerateAsync_SdxlLoraWithoutClipSkip_RewiresBothTextEncodesAndTheSampler()
    {
        var graph = await SubmitAsync(Model(SceneImageModelFamily.Sdxl, [Becky]));

        Assert.Equal("LoraLoader", graph["20"]!["class_type"]!.GetValue<string>());
        Assert.Equal("20", graph["3"]!["inputs"]!["model"]![0]!.GetValue<string>());
        // With no CLIPSetLastLayer there is no single clip node to intercept: BOTH encodes are the consumers.
        Assert.Equal("20", graph["6"]!["inputs"]!["clip"]![0]!.GetValue<string>());
        Assert.Equal("20", graph["7"]!["inputs"]!["clip"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task GenerateAsync_SdxlLoraWithClipSkip_RewiresTheClipSkipNodeAndLeavesTheEncodesOnIt()
    {
        var graph = await SubmitAsync(
            Model(SceneImageModelFamily.Sdxl, [Becky]),
            new SceneImageGenerationOptions { ClipSkip = -2 });

        Assert.True(graph.AsObject().ContainsKey("13"), "The clip-skip node should exist when a clip skip is set.");
        Assert.Equal("20", graph["13"]!["inputs"]!["clip"]![0]!.GetValue<string>());
        Assert.Equal("13", graph["6"]!["inputs"]!["clip"]![0]!.GetValue<string>());
        Assert.Equal("13", graph["7"]!["inputs"]!["clip"]![0]!.GetValue<string>());
        Assert.Equal("20", graph["3"]!["inputs"]!["model"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task GenerateAsync_TwoActorLoras_ChainsOneNodePerCharacterInSelectedOrder()
    {
        // A multi-character frame is N bindings, one per character: each LoRA takes model+clip from the previous
        // node, so the two identities are applied in the order the operator listed them, not in parallel.
        var graph = await SubmitAsync(Model(SceneImageModelFamily.Sdxl, [Becky, Dean]));

        Assert.Equal("dgc_lora_becky.safetensors", graph["20"]!["inputs"]!["lora_name"]!.GetValue<string>());
        Assert.Equal("dgc_lora_dean.safetensors", graph["21"]!["inputs"]!["lora_name"]!.GetValue<string>());
        Assert.Equal(0.65, graph["21"]!["inputs"]!["strength_model"]!.GetValue<double>());
        Assert.Equal("20", graph["21"]!["inputs"]!["model"]![0]!.GetValue<string>());
        Assert.Equal("20", graph["21"]!["inputs"]!["clip"]![0]!.GetValue<string>());
        // Every consumer follows the LAST node of the chain.
        Assert.Equal("21", graph["3"]!["inputs"]!["model"]![0]!.GetValue<string>());
        Assert.Equal("21", graph["6"]!["inputs"]!["clip"]![0]!.GetValue<string>());
        Assert.Equal("21", graph["7"]!["inputs"]!["clip"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task GenerateAsync_LoraOnAFamilyWithNoLoRAWiring_FailsFastRatherThanDroppingIt()
    {
        // A render that quietly ignored the selected identity would look exactly like one that applied it, so the
        // unsupported family must refuse the render instead.
        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => SubmitAsync(Model(SceneImageModelFamily.Flux, [Becky])));

        Assert.Contains("no LoRA", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeEncryption : IApiKeyEncryptionService
    {
        public string Encrypt(string plainTextApiKey) => "enc:" + plainTextApiKey;
        public string Decrypt(string encryptedApiKey) => encryptedApiKey.Replace("enc:", "", StringComparison.Ordinal);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler);
        public HttpClient CreateClient(string name) => _client;
    }
}
