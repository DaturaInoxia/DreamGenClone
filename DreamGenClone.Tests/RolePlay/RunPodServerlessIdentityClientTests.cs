using System.Net;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RunPodServerlessIdentityClientTests
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

    private static ResolvedIdentityImageModel Resolve() => new(
        ProviderBaseUrl: "https://api.runpod.ai/v2/identity-endpoint",
        ProviderTimeoutSeconds: 5,
        ModelIdentifier: "juggernautXL_ragnarok.safetensors",
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        ProviderName: "Identity Serverless",
        Mechanism: SceneImageIdentityMechanism.IpAdapter,
        AdapterRef: "PLUS FACE (portraits)",
        ClipVisionRef: null,
        IdentityStrength: 0.8,
        ApiKeyEncrypted: "enc:sekret",
        ImageProtocol: ImageProtocol.ComfyUiServerless);

    private static IdentityControlledImageRequest Request() => new()
    {
        PositivePrompt = "Photorealistic portrait",
        NegativePrompt = "deformed, bad anatomy",
        Size = "1024x1024",
        Seed = 42,
        ReferenceImageBytes = [1, 2, 3],
        CorrelationId = "identity-test"
    };

    private static RunPodServerlessIdentityClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(
            new FakeHttpClientFactory(new StubHttpMessageHandler(responder)),
            new FakeEncryption(),
            NullLogger<RunPodServerlessIdentityClient>.Instance);

    [Fact]
    public async Task GenerateAsync_CancellationDuringPolling_CancelsRunPodJobAndPropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        HttpRequestMessage? cancelRequest = null;
        var client = BuildClient(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/run"))
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(50);
                    cancellationSource.Cancel();
                });
                return JsonResponse("{\"id\":\"job-identity-cancel\"}");
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/cancel/job-identity-cancel"))
            {
                cancelRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return JsonResponse("{\"status\":\"RUNNING\"}");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateAsync(
            Resolve(), Request(), cancellationSource.Token));

        Assert.NotNull(cancelRequest);
        Assert.Equal("Bearer sekret", cancelRequest!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task GenerateAsync_MalformedBase64Output_ThrowsWithReasonCode()
    {
        var client = BuildClient(request => request.RequestUri!.AbsolutePath.EndsWith("/run")
            ? JsonResponse("{\"id\":\"job-identity-bad-base64\"}")
            : JsonResponse("{\"status\":\"COMPLETED\",\"output\":{\"images\":[{\"type\":\"base64\",\"data\":\"not-base64!!\"}]}}"));

        var exception = await Assert.ThrowsAsync<ImageGenerationException>(() =>
            client.GenerateAsync(Resolve(), Request(), CancellationToken.None));

        Assert.Equal("runpod_bad_base64", exception.ReasonCode);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonNode.Parse(json)!.ToJsonString())
    };
}
