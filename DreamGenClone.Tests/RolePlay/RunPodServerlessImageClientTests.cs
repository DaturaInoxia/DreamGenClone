using System.Net;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RunPodServerlessImageClientTests
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

    private static ResolvedImageModel Resolve(string? apiKey = "enc:sekret") => new(
        ProviderBaseUrl: "https://api.runpod.ai/v2/biglust-endpoint",
        ImageGenerationPath: "/run",
        ProviderTimeoutSeconds: 5,
        ApiKeyEncrypted: apiKey,
        ModelIdentifier: "bigLust_v16.safetensors",
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        ProviderName: "BigLust Serverless",
        IsSessionOverride: false,
        ImageProtocol: ImageProtocol.ComfyUiServerless);

    private static RunPodServerlessImageClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(
            new FakeHttpClientFactory(new StubHttpMessageHandler(responder)),
            new FakeEncryption(),
            NullLogger<RunPodServerlessImageClient>.Instance);

    [Fact]
    public async Task GenerateAsync_SubmitsOfficialWorkflowAndDecodesImage()
    {
        var pngBytes = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        HttpRequestMessage? submitRequest = null;
        var client = BuildClient(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/run"))
            {
                submitRequest = request;
                return JsonResponse("{\"id\":\"job-123\"}");
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/status/job-123"))
            {
                var encoded = Convert.ToBase64String(pngBytes);
                    return JsonResponse($"{{\"status\":\"COMPLETED\",\"output\":{{\"images\":[{{\"filename\":\"out.png\",\"type\":\"base64\",\"data\":\"data:image/png;base64,{encoded}\"}}]}}}}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await client.GenerateAsync(
            Resolve(),
            "adult-oriented portrait",
            "832x1216",
            "bad anatomy",
            seed: 24680L,
            CancellationToken.None);

        Assert.Equal(pngBytes, result);
        Assert.NotNull(submitRequest);
        Assert.Equal("Bearer sekret", submitRequest!.Headers.Authorization!.ToString());
        var body = await submitRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"workflow\"", body, StringComparison.Ordinal);
        Assert.Contains("bigLust_v16.safetensors", body, StringComparison.Ordinal);
        Assert.Contains("adult-oriented portrait", body, StringComparison.Ordinal);
        Assert.Contains("bad anatomy", body, StringComparison.Ordinal);
        Assert.Contains("\"seed\":24680", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_FailedJob_ThrowsWithReasonCode()
    {
        var client = BuildClient(request => request.RequestUri!.AbsolutePath.EndsWith("/run")
            ? JsonResponse("{\"id\":\"job-failed\"}")
            : JsonResponse("{\"status\":\"FAILED\"}"));

        var exception = await Assert.ThrowsAsync<ImageGenerationException>(() =>
            client.GenerateAsync(Resolve(), "prompt", null, cancellationToken: CancellationToken.None));

        Assert.Equal("runpod_job_failed", exception.ReasonCode);
    }

    [Fact]
    public async Task GenerateAsync_MissingApiKey_FailsBeforeHttpCall()
    {
        var called = false;
        var client = BuildClient(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var exception = await Assert.ThrowsAsync<ImageGenerationException>(() =>
            client.GenerateAsync(Resolve(null), "prompt", null, cancellationToken: CancellationToken.None));

        Assert.Equal("missing_serverless_api_key", exception.ReasonCode);
        Assert.False(called);
    }

    [Fact]
    public async Task CheckImageModelHealthAsync_UsesServerlessHealthPath()
    {
        HttpRequestMessage? requestSeen = null;
        var client = BuildClient(request =>
        {
            requestSeen = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await client.CheckImageModelHealthAsync(
            Resolve().ProviderBaseUrl,
            "/run",
            5,
            "sekret",
            Resolve().ModelIdentifier,
            ImageContentPolicy.AdultAllowed,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(requestSeen);
        Assert.Equal("/v2/biglust-endpoint/health", requestSeen!.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer sekret", requestSeen.Headers.Authorization!.ToString());
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonNode.Parse(json)!.ToJsonString())
    };
}
