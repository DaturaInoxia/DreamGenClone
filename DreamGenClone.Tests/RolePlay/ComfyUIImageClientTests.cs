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
}
