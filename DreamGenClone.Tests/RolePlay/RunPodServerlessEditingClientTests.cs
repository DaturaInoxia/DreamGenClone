using System.Net;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RunPodServerlessEditingClientTests
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

    private static ResolvedImageEditorModel Resolve() => new(
        ComfyUiUrl: "https://api.runpod.ai/v2/editor-endpoint",
        ProviderTimeoutSeconds: 5,
        ApiKeyEncrypted: "enc:sekret",
        ModelIdentifier: "qwen-image-edit-2511",
        ProviderName: "Qwen Editor Serverless",
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        DiffusionModel: "Qwen-Rapid-AIO-NSFW-v23.safetensors",
        TextEncoder: "qwen_2.5_vl_7b_fp8_scaled.safetensors",
        Vae: "qwen_image_vae.safetensors",
        Steps: 8,
        Cfg: 1.0,
        Sampler: "euler_ancestral",
        Scheduler: "beta",
        Denoise: 1.0,
        AuraFlowShift: 3.1,
        CfgNormStrength: 1.0,
        ImageProtocol: ImageProtocol.ComfyUiServerless);

    private static RunPodServerlessEditingClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(
            new FakeHttpClientFactory(new StubHttpMessageHandler(responder)),
            new FakeEncryption(),
            NullLogger<RunPodServerlessEditingClient>.Instance);

    [Fact]
    public async Task EditAsync_CancellationDuringPolling_CancelsRunPodJobAndPropagatesCancellation()
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
                return JsonResponse("{\"id\":\"job-edit-cancel\"}");
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/cancel/job-edit-cancel"))
            {
                cancelRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return JsonResponse("{\"status\":\"RUNNING\"}");
        });

        await using var source = new MemoryStream(new byte[] { 1, 2, 3 });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.EditAsync(
            Resolve(), source, "source.png", "Preserve the composition.", cancellationSource.Token));

        Assert.NotNull(cancelRequest);
        Assert.Equal("Bearer sekret", cancelRequest!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task EditAsync_MalformedBase64Output_ThrowsWithReasonCode()
    {
        var client = BuildClient(request => request.RequestUri!.AbsolutePath.EndsWith("/run")
            ? JsonResponse("{\"id\":\"job-edit-bad-base64\"}")
            : JsonResponse("{\"status\":\"COMPLETED\",\"output\":{\"images\":[{\"type\":\"base64\",\"data\":\"not-base64!!\"}]}}"));

        await using var source = new MemoryStream(new byte[] { 1, 2, 3 });
        var exception = await Assert.ThrowsAsync<ImageGenerationException>(() => client.EditAsync(
            Resolve(), source, "source.png", "Preserve the composition.", CancellationToken.None));

        Assert.Equal("runpod_bad_base64", exception.ReasonCode);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonNode.Parse(json)!.ToJsonString())
    };
}
