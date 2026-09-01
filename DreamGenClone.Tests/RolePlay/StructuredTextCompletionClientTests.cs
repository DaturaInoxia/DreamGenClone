using System.Net;
using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class StructuredTextCompletionClientTests
{
    [Fact]
    public async Task GenerateAsync_SendsExactStrictSchemaAndConfiguredAnalyzerSnapshot()
    {
        string? capturedBody = null;
        HttpRequestMessage? capturedRequest = null;
        var client = BuildClient(async request =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, """
                {"model":"structured-model","choices":[{"message":{"content":"{\"schemaVersion\":1}"},"finish_reason":"stop"}]}
                """);
        });
        var request = CreateRequest();

        var result = await client.GenerateAsync(CreateAnalyzer(), request);

        Assert.Equal("{\"schemaVersion\":1}", result.Content);
        Assert.Equal("structured-model", result.ModelIdentifier);
        Assert.Equal("stop", result.FinishReason);
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://structured.test/v1/chat/completions", capturedRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer secret", capturedRequest.Headers.Authorization!.ToString());
        var body = JsonSerializer.Deserialize<JsonElement>(capturedBody!);
        Assert.Equal("structured-model", body.GetProperty("model").GetString());
        Assert.Equal(0.2, body.GetProperty("temperature").GetDouble());
        Assert.Equal(0.8, body.GetProperty("top_p").GetDouble());
        Assert.Equal(4096, body.GetProperty("max_tokens").GetInt32());
        Assert.False(body.GetProperty("chat_template_kwargs").GetProperty("thinking").GetBoolean());
        var responseFormat = body.GetProperty("response_format");
        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
        var jsonSchema = responseFormat.GetProperty("json_schema");
        Assert.Equal("scene_beat_catalogue_v1", jsonSchema.GetProperty("name").GetString());
        Assert.True(jsonSchema.GetProperty("strict").GetBoolean());
        Assert.True(JsonElement.DeepEquals(request.ResponseSchema, jsonSchema.GetProperty("schema")));
    }

    [Fact]
    public async Task GenerateAsync_JsonObjectModeSendsSchemaInSystemMessage()
    {
        string? capturedBody = null;
        var client = BuildClient(async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, """
                {"model":"structured-model","choices":[{"message":{"content":"{\"schemaVersion\":1}"},"finish_reason":"stop"}]}
                """);
        });
        var request = CreateRequest();

        await client.GenerateAsync(
            CreateAnalyzer() with { StructuredOutputMode = StructuredOutputMode.JsonObject },
            request);

        var body = JsonSerializer.Deserialize<JsonElement>(capturedBody!);
        var responseFormat = body.GetProperty("response_format");
        Assert.Equal("json_object", responseFormat.GetProperty("type").GetString());
        Assert.False(responseFormat.TryGetProperty("json_schema", out _));
        var systemMessage = body.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.Contains("exactly one JSON object", systemMessage, StringComparison.Ordinal);
        Assert.Contains(request.ResponseSchemaName, systemMessage, StringComparison.Ordinal);
        Assert.Contains(request.ResponseSchema.GetRawText(), systemMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_SessionOverrideFailsBeforeHttp()
    {
        var requests = 0;
        var client = BuildClient(_ =>
        {
            requests++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}"));
        });
        var analyzer = CreateAnalyzer() with { Model = CreateAnalyzer().Model with { IsSessionOverride = true } };

        var exception = await Assert.ThrowsAsync<StructuredTextCompletionException>(
            () => client.GenerateAsync(analyzer, CreateRequest()));

        Assert.Contains("session model override", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task GenerateAsync_NonObjectSchemaFailsBeforeHttp()
    {
        var requests = 0;
        var client = BuildClient(_ =>
        {
            requests++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}"));
        });
        using var document = JsonDocument.Parse("[]");
        var request = CreateRequest() with { ResponseSchema = document.RootElement.Clone() };

        await Assert.ThrowsAsync<StructuredTextCompletionException>(
            () => client.GenerateAsync(CreateAnalyzer(), request));

        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData("{not-json", "malformed JSON")]
    [InlineData("{\"model\":\"structured-model\",\"choices\":[]}", "completion shape")]
    [InlineData("{\"model\":\"other-model\",\"choices\":[{\"message\":{\"content\":\"{}\"}}]}", "model identity")]
    public async Task GenerateAsync_InvalidProviderResponseFails(string responseBody, string expectedMessage)
    {
        var client = BuildClient(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, responseBody)));

        var exception = await Assert.ThrowsAsync<StructuredTextCompletionException>(
            () => client.GenerateAsync(CreateAnalyzer(), CreateRequest()));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_HttpFailureDoesNotExposeResponseBodyOrSecret()
    {
        var client = BuildClient(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.InternalServerError,
            "secret provider response")));

        var exception = await Assert.ThrowsAsync<StructuredTextCompletionException>(
            () => client.GenerateAsync(CreateAnalyzer(), CreateRequest()));

        Assert.DoesNotContain("secret provider response", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("enc:secret", exception.ToString(), StringComparison.Ordinal);
    }

    private static OpenAiStructuredTextCompletionClient BuildClient(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) => new(
            new FakeHttpClientFactory(new StubHttpMessageHandler(responder)),
            new FakeEncryption(),
            NullLogger<OpenAiStructuredTextCompletionClient>.Instance);

    private static ResolvedSceneBeatAnalyzer CreateAnalyzer()
    {
        var model = new ResolvedModel(
            "https://structured.test",
            "/v1/chat/completions",
            30,
            "enc:secret",
            "structured-model",
            0.2,
            0.8,
            4096,
            "Structured Provider",
            IsSessionOverride: false)
        {
            SupportsThinkingControl = true,
            ThinkingMode = ThinkingMode.Disabled
        };
        return new ResolvedSceneBeatAnalyzer(
            "function-default-1",
            "model-1",
            "provider-1",
            model,
            StructuredOutputMode.StrictJsonSchema,
            131072,
            8192,
            2,
            120,
            250,
            [5, 30],
            30,
            8);
    }

    private static StructuredTextCompletionRequest CreateRequest()
    {
        using var document = JsonDocument.Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["schemaVersion"],
              "properties": { "schemaVersion": { "const": 1 } }
            }
            """);
        return new StructuredTextCompletionRequest(
            "Extract catalogue beats.",
            "Authoritative snapshot",
            "scene_beat_catalogue_v1",
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
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await responder(request);
            response.RequestMessage = request;
            return response;
        }
    }
}