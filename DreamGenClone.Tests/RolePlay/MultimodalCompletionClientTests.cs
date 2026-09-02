using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class MultimodalCompletionClientTests
{
    [Fact]
    public async Task GenerateAsync_SendsOneImageAndStrictSchema()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var client = BuildClient(async request =>
        {
            captured = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, """
                {"model":"qwen-vl","choices":[{"message":{"content":"{\"status\":\"ready\"}"}}]}
                """);
        });
        var image = CreateImage();

        var result = await client.GenerateAsync(CreateModel(), CreateRequest(image));

        Assert.Equal("{\"status\":\"ready\"}", result.Content);
        Assert.NotNull(captured);
        Assert.Equal("Bearer secret", captured!.Headers.Authorization!.ToString());
        var body = JsonSerializer.Deserialize<JsonElement>(capturedBody!);
        Assert.Equal("qwen-vl", body.GetProperty("model").GetString());
        Assert.Equal(0.2, body.GetProperty("temperature").GetDouble());
        Assert.Equal(0.8, body.GetProperty("top_p").GetDouble());
        Assert.Equal(512, body.GetProperty("max_tokens").GetInt32());
        var messages = body.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        var parts = messages[1].GetProperty("content");
        Assert.Equal(2, parts.GetArrayLength());
        Assert.Equal("text", parts[0].GetProperty("type").GetString());
        Assert.Equal("image_url", parts[1].GetProperty("type").GetString());
        Assert.StartsWith("data:image/png;base64,", parts[1].GetProperty("image_url").GetProperty("url").GetString());
        var responseFormat = body.GetProperty("response_format");
        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
        Assert.True(responseFormat.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.Equal("edit_compilation", responseFormat.GetProperty("json_schema").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("image/gif", 4, 4, false, "media type")]
    [InlineData("image/png", 5000, 4, false, "dimensions")]
    [InlineData("image/png", 4, 4, true, "checksum")]
    public async Task GenerateAsync_InvalidImage_FailsBeforeHttp(
        string mediaType,
        int width,
        int height,
        bool corruptHash,
        string expectedMessage)
    {
        var requests = 0;
        var client = BuildClient(_ =>
        {
            requests++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}"));
        });
        var image = CreateImage(mediaType, width, height, corruptHash);

        var exception = await Assert.ThrowsAsync<MultimodalCompletionException>(
            () => client.GenerateAsync(CreateModel(), CreateRequest(image)));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task GenerateAsync_ResponseExceedsLimit_Fails()
    {
        var client = BuildClient(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, new string('x', 200))));
        var model = CreateModel() with { MaximumResponseBytes = 32 };

        var exception = await Assert.ThrowsAsync<MultimodalCompletionException>(
            () => client.GenerateAsync(model, CreateRequest(CreateImage())));

        Assert.Contains("byte limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_ReturnedModelDoesNotMatch_Fails()
    {
        var client = BuildClient(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, """
            {"model":"other-model","choices":[{"message":{"content":"{}"}}]}
            """)));

        var exception = await Assert.ThrowsAsync<MultimodalCompletionException>(
            () => client.GenerateAsync(CreateModel(), CreateRequest(CreateImage())));

        Assert.Contains("model identity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_ConfiguredContractAndIdentityMatch_Succeeds()
    {
        HttpRequestMessage? captured = null;
        var client = BuildClient(request =>
        {
            captured = request;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {"object":"list","data":[{"id":"qwen-vl","ready":true,"extra":"ignored"}]}
                """));
        });
        var model = CreateModel() with
        {
            ReadinessSuccessContractJson = """{"data":[{"id":"qwen-vl","ready":true}]}"""
        };

        await client.CheckHealthAsync(model);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("https://vision.test/v1/models", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task CheckHealthAsync_ContractDoesNotProveModel_FailsBeforeHttp()
    {
        var requests = 0;
        var client = BuildClient(_ =>
        {
            requests++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}"));
        });
        var model = CreateModel() with { ReadinessSuccessContractJson = """{"ready":true}""" };

        var exception = await Assert.ThrowsAsync<MultimodalCompletionException>(() => client.CheckHealthAsync(model));

        Assert.Contains("exact model identity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task GenerateAsync_CallerCancellation_IsPreserved()
    {
        var client = BuildClient(async request =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, request.GetCancellationToken());
            return JsonResponse(HttpStatusCode.OK, "{}");
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GenerateAsync(CreateModel(), CreateRequest(CreateImage()), cancellation.Token));
    }

    [Fact]
    public async Task GenerateAsync_ConfiguredTimeout_IsEnforced()
    {
        var client = BuildClient(async request =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, request.GetCancellationToken());
            return JsonResponse(HttpStatusCode.OK, "{}");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GenerateAsync(CreateModel() with { RequestTimeoutSeconds = 1 }, CreateRequest(CreateImage())));
    }

    [Fact]
    public async Task GenerateAsync_FailureDiagnosticsExcludeSecretAndResponseBody()
    {
        var logger = new ListLogger<OpenAiMultimodalCompletionClient>();
        var client = BuildClient(
            _ => Task.FromResult(JsonResponse(HttpStatusCode.InternalServerError, "data:image/png;base64,private-image")),
            logger);

        var exception = await Assert.ThrowsAsync<MultimodalCompletionException>(
            () => client.GenerateAsync(CreateModel(), CreateRequest(CreateImage())));
        var diagnostics = exception + string.Join(Environment.NewLine, logger.Messages);

        Assert.DoesNotContain("private-image", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("https://vision.test", diagnostics, StringComparison.Ordinal);
    }

    private static OpenAiMultimodalCompletionClient BuildClient(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder,
        ILogger<OpenAiMultimodalCompletionClient>? logger = null) => new(
            new FakeHttpClientFactory(new StubHttpMessageHandler(responder)),
            new FakeEncryption(),
            logger ?? NullLogger<OpenAiMultimodalCompletionClient>.Instance);

    private static ResolvedMultimodalModel CreateModel() => new(
        "provider-1",
        "model-1",
        "https://vision.test",
        "/v1/chat/completions",
        "/v1/models",
        """{"data":[{"id":"qwen-vl"}]}""",
        30,
        420,
        30,
        "vision-api-key",
        "enc:secret",
        "qwen-vl",
        "Vision",
        ImageContentPolicy.AdultAllowed,
        ModelLifecycleStrategy.ScheduledSinglePod,
        1,
        1024,
        4096,
        64,
        new HashSet<string>(["image/png", "image/jpeg"], StringComparer.OrdinalIgnoreCase),
        1024,
        1,
        4,
        0.2,
        0.8,
        512,
        "vllm-revision",
        "model-revision");

    private static MultimodalImageInput CreateImage(
        string mediaType = "image/png",
        int width = 4,
        int height = 4,
        bool corruptHash = false)
    {
        var bytes = Encoding.UTF8.GetBytes("image-bytes");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return new MultimodalImageInput(mediaType, bytes, width, height, corruptHash ? new string('0', 64) : hash);
    }

    private static MultimodalCompletionRequest CreateRequest(MultimodalImageInput image)
    {
        using var document = JsonDocument.Parse("""{"type":"object","additionalProperties":false}""");
        return new MultimodalCompletionRequest(
            "Compile the edit.",
            "Change the shirt to red.",
            image,
            "edit_compilation",
            document.RootElement.Clone());
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string content) => new(statusCode)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class FakeEncryption : IApiKeyEncryptionService
    {
        public string Encrypt(string plainTextApiKey) => "enc:" + plainTextApiKey;
        public string Decrypt(string encryptedApiKey) => encryptedApiKey.Replace("enc:", "", StringComparison.Ordinal);
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.SetCancellationToken(cancellationToken);
            var response = await responder(request);
            response.RequestMessage = request;
            return response;
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}

internal static class HttpRequestMessageCancellationExtensions
{
    private static readonly HttpRequestOptionsKey<CancellationToken> CancellationTokenKey = new("TestCancellationToken");

    public static void SetCancellationToken(this HttpRequestMessage request, CancellationToken cancellationToken) =>
        request.Options.Set(CancellationTokenKey, cancellationToken);

    public static CancellationToken GetCancellationToken(this HttpRequestMessage request) =>
        request.Options.TryGetValue(CancellationTokenKey, out var cancellationToken)
            ? cancellationToken
            : CancellationToken.None;
}