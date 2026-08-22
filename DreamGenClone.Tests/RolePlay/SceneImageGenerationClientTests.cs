using System.Net;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageGenerationClientTests
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
        ProviderBaseUrl: "https://api.together.ai",
        ImageGenerationPath: "/v1/images/generations",
        ProviderTimeoutSeconds: 30,
        ApiKeyEncrypted: "enc:sekret",
        ModelIdentifier: "black-forest-labs/FLUX.1-schnell",
        ContentPolicy: ImageContentPolicy.SfwFiltered,
        ProviderName: "Together",
        IsSessionOverride: false);

    private static ImageGenerationClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new FakeHttpClientFactory(new StubHttpMessageHandler(responder)), new FakeEncryption(), NullLogger<ImageGenerationClient>.Instance);

    [Fact]
    public async Task GenerateAsync_Success_DecodesBytesAndSendsExpectedRequest()
    {
        var b64 = Convert.ToBase64String(new byte[] { 10, 20, 30 });
        HttpRequestMessage? captured = null;
        var client = BuildClient(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { data = new[] { new { b64_json = b64 } } }))
            };
        });

        var bytes = await client.GenerateAsync(Resolve(), "a cat in a hat", "1024x1024", CancellationToken.None);

        Assert.NotNull(bytes);
        Assert.Equal(new byte[] { 10, 20, 30 }, bytes);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("https://api.together.ai/v1/images/generations", captured.RequestUri!.ToString());
        Assert.Equal("Bearer sekret", captured.Headers.Authorization!.ToString());

        var body = JsonSerializer.Deserialize<JsonElement>(await captured.Content!.ReadAsStringAsync());
        Assert.Equal("black-forest-labs/FLUX.1-schnell", body.GetProperty("model").GetString());
        Assert.Equal("a cat in a hat", body.GetProperty("prompt").GetString());
        Assert.Equal(1024, body.GetProperty("width").GetInt32());
        Assert.Equal(1024, body.GetProperty("height").GetInt32());
        Assert.Equal("base64", body.GetProperty("response_format").GetString());
        Assert.Equal(1, body.GetProperty("n").GetInt32());
    }

    [Theory]
    [InlineData(401, "Invalid API key", "invalid_api_key")]
    [InlineData(429, "Rate limit exceeded", "rate_limit")]
    [InlineData(402, "Payment required", "payment_required")]
    [InlineData(500, "Server error", "server_error")]
    public async Task GenerateAsync_HttpErrors_MapToImageGenerationException(int statusCode, string messagePart, string reasonCode)
    {
        var client = BuildClient(_ => new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent("{\"error\":\"nope\"}")
        });

        var ex = await Assert.ThrowsAsync<ImageGenerationException>(
            () => client.GenerateAsync(Resolve(), "prompt", null, CancellationToken.None));

        Assert.Contains(messagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(statusCode, ex.StatusCode);
        Assert.Equal(reasonCode, ex.ReasonCode);
        Assert.Equal("Together", ex.ProviderName);
    }

    [Fact]
    public async Task GenerateAsync_NoImageData_ReturnsNull()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { data = Array.Empty<object>() }))
        });

        var bytes = await client.GenerateAsync(Resolve(), "prompt", null, CancellationToken.None);
        Assert.Null(bytes);
    }

    [Fact]
    public async Task GenerateAsync_NoApiKey_OmitsAuthorizationHeader()
    {
        HttpRequestMessage? captured = null;
        var client = BuildClient(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(new byte[] { 1 }) } } }))
            };
        });

        var model = Resolve() with { ApiKeyEncrypted = null };
        await client.GenerateAsync(model, "prompt", null, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Null(captured!.Headers.Authorization);
    }

    [Fact]
    public async Task CheckImageModelHealthAsync_Success_ProbesImagePath()
    {
        var captured = new List<JsonElement>();
        var client = BuildClient(req =>
        {
            captured.Add(JsonSerializer.Deserialize<JsonElement>(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult()));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(new byte[] { 1 }) } } }))
            };
        });

        var (success, message) = await client.CheckImageModelHealthAsync(
            "https://api.together.ai", "/v1/images/generations", 30, "sekret",
            "ByteDance-Seed/Seedream-4.0", ImageContentPolicy.AdultAllowed, CancellationToken.None);

        Assert.True(success);
        Assert.Equal(3, captured.Count);
        Assert.Equal("ByteDance-Seed/Seedream-4.0", captured[0].GetProperty("model").GetString());
        Assert.Equal("base64", captured[0].GetProperty("response_format").GetString());
        Assert.False(captured[0].TryGetProperty("negative_prompt", out _));
        Assert.False(captured[0].TryGetProperty("disable_safety_checker", out _));
        Assert.Equal("blurry, distorted, extra fingers", captured[1].GetProperty("negative_prompt").GetString());
        Assert.False(captured[1].TryGetProperty("disable_safety_checker", out _));
        Assert.True(captured[2].GetProperty("disable_safety_checker").GetBoolean());
        Assert.False(captured[2].TryGetProperty("negative_prompt", out _));
    }

    [Fact]
    public async Task CheckImageModelHealthAsync_HttpError_ReturnsFailure()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"bad\"}")
        });

        var (success, message) = await client.CheckImageModelHealthAsync(
            "https://api.together.ai", "/v1/images/generations", 30, "sekret",
            "ByteDance-Seed/Seedream-4.0", ImageContentPolicy.AdultAllowed, CancellationToken.None);

        Assert.False(success);
        Assert.Contains("400", message);
    }
}
