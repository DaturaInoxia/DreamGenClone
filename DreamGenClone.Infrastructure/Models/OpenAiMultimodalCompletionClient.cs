using System.Buffers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

public sealed class OpenAiMultimodalCompletionClient : IMultimodalCompletionClient
{
    private const string HttpClientName = "MultimodalCompletionClient";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<OpenAiMultimodalCompletionClient> _logger;

    public OpenAiMultimodalCompletionClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<OpenAiMultimodalCompletionClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<MultimodalCompletionResult> GenerateAsync(
        ResolvedMultimodalModel model,
        MultimodalCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(model, request);
        var stopwatch = Stopwatch.StartNew();
        using var client = CreateClient(model);
        var payload = new ChatCompletionRequest(
            model.ModelIdentifier,
            [
                new("system", request.SystemMessage),
                new("user", new object[]
                {
                    new TextContentPart("text", request.UserMessage),
                    new ImageContentPart("image_url", new ImageUrl($"data:{request.Image.MediaType};base64,{Convert.ToBase64String(request.Image.Bytes.Span)}"))
                })
            ],
            model.Temperature,
            model.TopP,
            model.MaxTokens,
            new ResponseFormat("json_schema", new JsonSchema(request.ResponseSchemaName, true, request.ResponseSchema)));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, model.ChatCompletionsPath.TrimStart('/'))
        {
            Content = JsonContent.Create(payload)
        };
        using var response = await client.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new MultimodalCompletionException(
                $"Multimodal completion failed for provider '{model.ProviderName}' with HTTP {(int)response.StatusCode}.");
        }

        var body = await ReadBoundedAsync(response.Content, model.MaximumResponseBytes, cancellationToken).ConfigureAwait(false);
        ChatCompletionResponse parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(body)
                ?? throw new JsonException("The response body was null.");
        }
        catch (JsonException ex)
        {
            throw new MultimodalCompletionException("The multimodal provider returned malformed JSON.", ex);
        }

        if (parsed.Choices is not [{ Message.Content: { } content }]
            || string.IsNullOrWhiteSpace(content)
            || string.IsNullOrWhiteSpace(parsed.Model))
        {
            throw new MultimodalCompletionException("The multimodal provider returned an invalid completion shape.");
        }
        if (!string.Equals(parsed.Model, model.ModelIdentifier, StringComparison.Ordinal))
        {
            throw new MultimodalCompletionException("The multimodal provider returned an unexpected model identity.");
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Multimodal completion succeeded: Provider={ProviderName}, Model={ModelIdentifier}, DurationMs={DurationMs}",
            model.ProviderName,
            model.ModelIdentifier,
            stopwatch.ElapsedMilliseconds);
        return new MultimodalCompletionResult(content, parsed.Model, stopwatch.Elapsed);
    }

    public async Task CheckHealthAsync(
        ResolvedMultimodalModel model,
        CancellationToken cancellationToken = default)
    {
        JsonElement expected;
        try
        {
            expected = JsonSerializer.Deserialize<JsonElement>(model.ReadinessSuccessContractJson);
        }
        catch (JsonException ex)
        {
            throw new MultimodalCompletionException("The configured readiness success contract is invalid JSON.", ex);
        }
        if (expected.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            throw new MultimodalCompletionException("The configured readiness success contract must be a JSON object or array.");
        if (!ContainsString(expected, model.ModelIdentifier))
            throw new MultimodalCompletionException("The configured readiness success contract does not prove the exact model identity.");

        using var client = CreateClient(model);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, model.ReadinessPath.TrimStart('/'));
        using var response = await client.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new MultimodalCompletionException(
                $"Multimodal readiness check failed for provider '{model.ProviderName}' with HTTP {(int)response.StatusCode}.");
        }

        var body = await ReadBoundedAsync(response.Content, model.MaximumResponseBytes, cancellationToken).ConfigureAwait(false);
        JsonElement actual;
        try
        {
            actual = JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (JsonException ex)
        {
            throw new MultimodalCompletionException("The multimodal readiness endpoint returned malformed JSON.", ex);
        }
        if (!MatchesConfiguredContract(expected, actual))
            throw new MultimodalCompletionException("The multimodal readiness response did not match the configured success contract.");

        _logger.LogInformation(
            "Multimodal readiness check succeeded: Provider={ProviderName}, Model={ModelIdentifier}",
            model.ProviderName,
            model.ModelIdentifier);
    }

    private HttpClient CreateClient(ResolvedMultimodalModel model)
    {
        if (string.IsNullOrWhiteSpace(model.ApiKeyEncrypted))
            throw new MultimodalCompletionException($"Provider '{model.ProviderName}' has no configured inference credential.");

        string apiKey;
        try
        {
            apiKey = _encryptionService.Decrypt(model.ApiKeyEncrypted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MultimodalCompletionException($"Provider '{model.ProviderName}' inference credential could not be decrypted.", ex);
        }
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new MultimodalCompletionException($"Provider '{model.ProviderName}' inference credential is empty.");

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(model.ProviderBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(model.RequestTimeoutSeconds);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static void ValidateRequest(ResolvedMultimodalModel model, MultimodalCompletionRequest request)
    {
        if (model.MaximumInputImages < 1)
            throw new MultimodalCompletionException("The resolved model does not permit the required source image.");
        if (string.IsNullOrWhiteSpace(request.SystemMessage) || string.IsNullOrWhiteSpace(request.UserMessage))
            throw new MultimodalCompletionException("Multimodal system and user messages are required.");
        if (string.IsNullOrWhiteSpace(request.ResponseSchemaName)
            || request.ResponseSchema.ValueKind != JsonValueKind.Object)
        {
            throw new MultimodalCompletionException("A named JSON object response schema is required.");
        }

        var image = request.Image;
        if (!model.AcceptedInputMediaTypes.Contains(image.MediaType))
            throw new MultimodalCompletionException("The source image media type is not accepted by the configured model.");
        if (image.Bytes.IsEmpty || image.Bytes.Length > model.MaximumInputImageBytes)
            throw new MultimodalCompletionException("The source image byte count is outside the configured limit.");
        if (image.Width <= 0 || image.Height <= 0
            || image.Width > model.MaximumInputImageDimension
            || image.Height > model.MaximumInputImageDimension)
        {
            throw new MultimodalCompletionException("The source image dimensions are outside the configured limit.");
        }
        if ((long)image.Width * image.Height > model.MaximumInputImagePixels)
            throw new MultimodalCompletionException("The source image pixel count exceeds the configured limit.");

        var actualHash = Convert.ToHexString(SHA256.HashData(image.Bytes.Span));
        if (!string.Equals(actualHash, image.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new MultimodalCompletionException("The source image checksum does not match its bytes.");
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maximumBytes)
            throw new MultimodalCompletionException("The multimodal provider response exceeds the configured byte limit.");

        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (destination.Length + read > maximumBytes)
                    throw new MultimodalCompletionException("The multimodal provider response exceeds the configured byte limit.");
                destination.Write(buffer, 0, read);
            }
            return destination.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool ContainsString(JsonElement element, string expected)
    {
        if (element.ValueKind == JsonValueKind.String)
            return string.Equals(element.GetString(), expected, StringComparison.Ordinal);
        if (element.ValueKind == JsonValueKind.Object)
            return element.EnumerateObject().Any(property => ContainsString(property.Value, expected));
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(item => ContainsString(item, expected));
        return false;
    }

    private static bool MatchesConfiguredContract(JsonElement expected, JsonElement actual) => expected.ValueKind switch
    {
        JsonValueKind.Object => actual.ValueKind == JsonValueKind.Object
            && expected.EnumerateObject().All(property =>
                actual.TryGetProperty(property.Name, out var actualValue)
                && MatchesConfiguredContract(property.Value, actualValue)),
        JsonValueKind.Array => actual.ValueKind == JsonValueKind.Array
            && expected.EnumerateArray().All(expectedItem =>
                actual.EnumerateArray().Any(actualItem => MatchesConfiguredContract(expectedItem, actualItem))),
        _ => JsonElement.DeepEquals(expected, actual)
    };

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("top_p")] double TopP,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("response_format")] ResponseFormat ResponseFormat);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] object Content);

    private sealed record TextContentPart(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);

    private sealed record ImageContentPart(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("image_url")] ImageUrl ImageUrl);

    private sealed record ImageUrl([property: JsonPropertyName("url")] string Url);

    private sealed record ResponseFormat(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("json_schema")] JsonSchema JsonSchema);

    private sealed record JsonSchema(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("strict")] bool Strict,
        [property: JsonPropertyName("schema")] JsonElement Schema);

    private sealed record ChatCompletionResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice([property: JsonPropertyName("message")] ChatResponseMessage? Message);
    private sealed record ChatResponseMessage([property: JsonPropertyName("content")] string? Content);
}