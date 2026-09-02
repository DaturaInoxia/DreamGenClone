using System.Net;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ComfyUIImageClientTests
{
    private sealed class FakeEncryption : IApiKeyEncryptionService
    {
        public string Encrypt(string plainTextApiKey) => "enc:" + plainTextApiKey;
        public string Decrypt(string encryptedApiKey) => encryptedApiKey.Replace("enc:", "");
    }

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

    private static ResolvedImageModel Resolve() => new(
        ProviderBaseUrl: "https://qguv5e029u58lb-3000.proxy.runpod.net",
        ImageGenerationPath: "/prompt",
        ProviderTimeoutSeconds: 30,
        ApiKeyEncrypted: "enc:sekret",
        ModelIdentifier: "ponyDiffusionV6XL_v6.safetensors",
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        ProviderName: "RunPod ComfyUI",
        IsSessionOverride: false,
        SceneImageModelFamily: SceneImageModelFamily.Pony,
        PromptDialect: SceneImagePromptDialect.PonyV6Tags,
        ImageProtocol: ImageProtocol.ComfyUi,
        ComfyUiUrl: "https://qguv5e029u58lb-3000.proxy.runpod.net");

    private static ComfyUIImageClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new FakeHttpClientFactory(new StubHttpMessageHandler(responder)), new FakeEncryption(), NullLogger<ComfyUIImageClient>.Instance);

    [Fact]
    public async Task GenerateAsync_Success_ReturnsPngBytes()
    {
        var pngBytes = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        var promptId = "abc-123";
        HttpRequestMessage? submitRequest = null;

        var client = BuildClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/prompt"))
            {
                submitRequest = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonNode.Parse($"{{\"prompt_id\":\"{promptId}\"}}")!.ToJsonString())
                };
            }
            if (req.RequestUri!.AbsolutePath.Contains($"/history/{promptId}"))
            {
                var history = JsonNode.Parse($$"""
                {
                  "{{promptId}}": {
                    "status": { "status_str": "success" },
                    "outputs": {
                      "9": { "images": [ { "filename": "out.png", "subfolder": "", "type": "output" } ] }
                    }
                  }
                }
                """);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(history!.ToJsonString())
                };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/view"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(pngBytes)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await client.GenerateAsync(Resolve(), "a nude couple", "1024x1024", "extra limbs, malformed anatomy", seed: 24680L, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(pngBytes, result);
        Assert.NotNull(submitRequest);
        Assert.Equal("POST", submitRequest!.Method.Method);
        // Bearer auth from decrypted key
        Assert.Equal("Bearer sekret", submitRequest.Headers.Authorization!.ToString());
        // Per-scene negative is injected into workflow node 7.
        var body = await submitRequest.Content!.ReadAsStringAsync();
        Assert.Contains("extra limbs, malformed anatomy", body, StringComparison.Ordinal);
        // Fixed seed is injected into the KSampler node.
        Assert.Contains("\"seed\":24680", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_WorkflowError_Throws()
    {
        var promptId = "err-1";
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/prompt"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonNode.Parse($"{{\"prompt_id\":\"{promptId}\"}}")!.ToJsonString())
                };
            }
            if (req.RequestUri!.AbsolutePath.Contains($"/history/{promptId}"))
            {
                var history = JsonNode.Parse($$"""
                {
                  "{{promptId}}": {
                    "status": { "status_str": "error" },
                    "outputs": {}
                  }
                }
                """);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(history!.ToJsonString())
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        await Assert.ThrowsAsync<ImageGenerationException>(() =>
            client.GenerateAsync(Resolve(), "prompt", null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_SubmitFails_Throws()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("bad gateway")
        });

        await Assert.ThrowsAsync<ImageGenerationException>(() =>
            client.GenerateAsync(Resolve(), "prompt", null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task CheckImageModelHealthAsync_Reachable_ReturnsSuccess()
    {
        var client = BuildClient(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/system_stats")
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var (success, _) = await client.CheckImageModelHealthAsync(
            "https://qguv5e029u58lb-3000.proxy.runpod.net", "/prompt", 30, "sekret", "pony", ImageContentPolicy.AdultAllowed, CancellationToken.None);

        Assert.True(success);
    }

    [Fact]
    public void BuildSdxlWorkflow_NoClipSkip_UsesJuggernautSettings()
    {
        var wf = ComfyUIImageClient.BuildSdxlWorkflow(
            "juggernautXL_ragnarok.safetensors",
            "a photorealistic man and woman on a beach",
            "deformed, four legs",
            "1024x1024",
            seed: 24680L);

        var json = wf.ToJsonString();

        // No Pony CLIP-skip node in the SDXL/Juggernaut workflow.
        Assert.DoesNotContain("CLIPSetLastLayer", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"stop_at_clip_layer\"", json, StringComparison.Ordinal);

        // Juggernaut-recommended sampler settings.
        Assert.Contains("\"dpmpp_2m_sde\"", json, StringComparison.Ordinal);
        Assert.Contains("\"scheduler\":\"karras\"", json, StringComparison.Ordinal);
        Assert.Contains("\"steps\":30", json, StringComparison.Ordinal);
        Assert.Contains("\"cfg\":5", json, StringComparison.Ordinal);

        // Checkpoint + prompt are injected.
        Assert.Contains("juggernautXL_ragnarok.safetensors", json, StringComparison.Ordinal);
        Assert.Contains("a photorealistic man and woman on a beach", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_ExplicitSdxlMetadataWithOpaqueCheckpoint_UsesSdxlWorkflow()
    {
        var pngBytes = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        var promptId = "sdxl-1";
        HttpRequestMessage? submitRequest = null;
        var client = BuildClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/prompt"))
            {
                submitRequest = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonNode.Parse($"{{\"prompt_id\":\"{promptId}\"}}")!.ToJsonString())
                };
            }
            if (req.RequestUri!.AbsolutePath.Contains($"/history/{promptId}"))
            {
                var history = JsonNode.Parse($$"""
                {
                  "{{promptId}}": {
                    "status": { "status_str": "success" },
                    "outputs": {
                      "9": { "images": [ { "filename": "out.png", "subfolder": "", "type": "output" } ] }
                    }
                  }
                }
                """);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(history!.ToJsonString())
                };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/view"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(pngBytes)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var model = Resolve() with
        {
            ModelIdentifier = "opaque-render-model-v42.safetensors",
            SceneImageModelFamily = SceneImageModelFamily.Sdxl,
            PromptDialect = SceneImagePromptDialect.SdxlNaturalLanguage
        };
        var result = await client.GenerateAsync(model, "photorealistic couple", "1024x1024", null, seed: 1L, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(submitRequest);
        var body = await submitRequest!.Content!.ReadAsStringAsync();
        // SDXL workflow: no CLIP skip, Juggernaut sampler, correct checkpoint wired in.
        Assert.DoesNotContain("CLIPSetLastLayer", body, StringComparison.Ordinal);
        Assert.Contains("\"dpmpp_2m_sde\"", body, StringComparison.Ordinal);
        Assert.Contains("opaque-render-model-v42.safetensors", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_NonFilenameIdentifier_ThrowsInvalidCheckpoint()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        });
        var model = Resolve() with { ModelIdentifier = "gpt-image-1" };

        var ex = await Assert.ThrowsAsync<ImageGenerationException>(() =>
            client.GenerateAsync(model, "prompt", null, null, null, CancellationToken.None));
        Assert.Equal("invalid_checkpoint_identifier", ex.ReasonCode);
    }

    [Fact]
    public async Task GenerateAsync_UnknownMetadata_ThrowsBeforeHttpCall()
    {
        var called = false;
        var client = BuildClient(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var model = Resolve() with
        {
            ModelIdentifier = "opaque-render-model-v42.safetensors",
            SceneImageModelFamily = SceneImageModelFamily.Unknown,
            PromptDialect = SceneImagePromptDialect.Unknown
        };

        var ex = await Assert.ThrowsAsync<ImageGenerationException>(() =>
            client.GenerateAsync(model, "prompt", null, null, null, CancellationToken.None));
        Assert.Equal("invalid_image_prompt_metadata", ex.ReasonCode);
        Assert.False(called);
    }
}
