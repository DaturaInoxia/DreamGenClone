using System.Net;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ImageEditingClientDispatcherTests
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

    private static ResolvedImageEditorModel Resolve(ImageProtocol protocol) => new(
        ComfyUiUrl: "https://editor.example.test",
        ProviderTimeoutSeconds: 10,
        ApiKeyEncrypted: "enc:sekret",
        ModelIdentifier: "qwen-image-edit-2511",
        ProviderName: "Qwen Editor",
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
        ImageProtocol: protocol);

    private static ImageEditingClientDispatcher BuildDispatcher(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = new FakeHttpClientFactory(new StubHttpMessageHandler(responder));
        var encryption = new FakeEncryption();
        return new ImageEditingClientDispatcher(
            new ComfyUIImageEditingClient(factory, encryption, NullLogger<ComfyUIImageEditingClient>.Instance),
            new RunPodServerlessEditingClient(factory, encryption, NullLogger<RunPodServerlessEditingClient>.Instance));
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Dispatcher_ComfyUiProtocol_UsesPodUploadFlow()
    {
        var pngBytes = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        var firstRequestPath = string.Empty;
        var dispatcher = BuildDispatcher(request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/upload/image"))
            {
                firstRequestPath = request.RequestUri.AbsolutePath;
                return JsonResponse("{\"name\":\"source.png\",\"subfolder\":\"\"}");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/prompt"))
            {
                return JsonResponse("{\"prompt_id\":\"p1\"}");
            }

            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("/history/"))
            {
                return JsonResponse(
                    "{\"p1\":{\"status\":{\"status_str\":\"success\"}," +
                    "\"outputs\":{\"9\":{\"images\":[{\"filename\":\"out.png\",\"subfolder\":\"\",\"type\":\"output\"}]}}}}");
            }

            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/view"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pngBytes) };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        await using var source = new MemoryStream(new byte[] { 9, 8, 7, 6, 5 });
        var result = await dispatcher.EditAsync(
            Resolve(ImageProtocol.ComfyUi), source, "source.png", "Rotate the head left.", CancellationToken.None);

        Assert.Equal(pngBytes, result);
        Assert.Equal("/upload/image", firstRequestPath);
    }

    [Fact]
    public async Task Dispatcher_ComfyUiServerlessProtocol_PostsRunWithInlineSource()
    {
        var pngBytes = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        HttpRequestMessage? submitRequest = null;
        string? submitBody = null;
        var dispatcher = BuildDispatcher(request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/run"))
            {
                submitRequest = request;
                return JsonResponse("{\"id\":\"job-123\"}");
            }

            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/status/job-123"))
            {
                var encoded = Convert.ToBase64String(pngBytes);
                return JsonResponse(
                    $"{{\"status\":\"COMPLETED\",\"output\":{{\"images\":[{{\"filename\":\"out.png\",\"type\":\"base64\",\"data\":\"data:image/png;base64,{encoded}\"}}]}}}}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        await using var source = new MemoryStream(new byte[] { 9, 8, 7, 6, 5 });
        var result = await dispatcher.EditAsync(
            Resolve(ImageProtocol.ComfyUiServerless), source, "source.png", "Rotate the head left.", CancellationToken.None);

        Assert.Equal(pngBytes, result);
        Assert.NotNull(submitRequest);
        Assert.Equal("Bearer sekret", submitRequest!.Headers.Authorization!.ToString());
        Assert.Equal("/run", submitRequest.RequestUri!.AbsolutePath);

        // The workflow + the inline source (input.images) must both be present on the /run payload.
        if (submitRequest.Content is not null)
        {
            submitBody = await submitRequest.Content.ReadAsStringAsync(CancellationToken.None);
        }

        Assert.NotNull(submitBody);
        var payload = JsonNode.Parse(submitBody!)!["input"]!;
        var workflow = payload["workflow"]!;
        Assert.NotNull(workflow);
        Assert.Equal("source.png", workflow["1"]!["inputs"]!["image"]!.GetValue<string>());
        var image = payload["images"]![0]!;
        Assert.Equal("source.png", image["name"]!.GetValue<string>());
        Assert.StartsWith("data:image/png;base64,", image["image"]!.GetValue<string>());

        // Serverless edits must use the AIO merged-checkpoint graph (CheckpointLoaderSimple, no
        // separate UNETLoader/CLIPLoader/VAELoader) or the AIO worker rejects it at validation.
        Assert.Equal("CheckpointLoaderSimple", workflow["16"]!["class_type"]!.GetValue<string>());
        Assert.Equal("Qwen-Rapid-AIO-NSFW-v23.safetensors", workflow["16"]!["inputs"]!["ckpt_name"]!.GetValue<string>());
        Assert.DoesNotContain("UNETLoader", workflow.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("CLIPLoader", workflow.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("VAELoader", workflow.ToJsonString(), StringComparison.Ordinal);
    }
}
