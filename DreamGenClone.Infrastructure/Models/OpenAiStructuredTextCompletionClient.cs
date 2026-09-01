using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

public sealed class OpenAiStructuredTextCompletionClient : IStructuredTextCompletionClient
{
    private const string HttpClientName = "StructuredTextCompletionClient";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<OpenAiStructuredTextCompletionClient> _logger;

    public OpenAiStructuredTextCompletionClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<OpenAiStructuredTextCompletionClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<StructuredTextCompletionResult> GenerateAsync(
        ResolvedSceneBeatAnalyzer analyzer,
        StructuredTextCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(analyzer, request);
        var resolved = analyzer.Model;
        var stopwatch = Stopwatch.StartNew();
        using var client = CreateClient(resolved);
        var systemMessage = analyzer.StructuredOutputMode == StructuredOutputMode.JsonObject
            ? BuildJsonObjectSystemMessage(request)
            : request.SystemMessage;
        var responseFormat = analyzer.StructuredOutputMode switch
        {
            StructuredOutputMode.StrictJsonSchema => new ResponseFormat(
                "json_schema",
                new JsonSchema(request.ResponseSchemaName, true, request.ResponseSchema)),
            StructuredOutputMode.JsonObject => new ResponseFormat("json_object", null),
            _ => throw new StructuredTextCompletionException(
                "structured_text_output_mode_unsupported",
                "The configured structured-output mode is unsupported.",
                false)
        };
        var payload = new ChatCompletionRequest(
            resolved.ModelIdentifier,
            [new("system", systemMessage), new("user", request.UserMessage)],
            resolved.Temperature,
            resolved.TopP,
            resolved.MaxTokens,
            responseFormat,
            new Dictionary<string, object> { ["thinking"] = resolved.ThinkingMode == ThinkingMode.Enabled });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, resolved.ChatCompletionsPath.TrimStart('/'))
        {
            Content = JsonContent.Create(payload)
        };
        using var response = await client.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new StructuredTextCompletionException(
                $"structured_text_http_{(int)response.StatusCode}",
                $"Structured text completion failed for provider '{resolved.ProviderName}' with HTTP {(int)response.StatusCode}.",
                response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500);
        }

        ChatCompletionResponse parsed;
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            parsed = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(body, cancellationToken: cancellationToken)
                .ConfigureAwait(false)
                ?? throw new JsonException("The response body was null.");
        }
        catch (JsonException ex)
        {
            throw new StructuredTextCompletionException(
                "structured_text_response_malformed",
                "The structured text provider returned malformed JSON.",
                false,
                ex);
        }

        if (parsed.Choices is not [{ Message.Content: { } content }]
            || string.IsNullOrWhiteSpace(content)
            || string.IsNullOrWhiteSpace(parsed.Model))
        {
            throw new StructuredTextCompletionException(
                "structured_text_response_shape_invalid",
                "The structured text provider returned an invalid completion shape.",
                false);
        }
        if (!string.Equals(parsed.Model, resolved.ModelIdentifier, StringComparison.Ordinal))
            throw new StructuredTextCompletionException(
                "structured_text_model_identity_mismatch",
                "The structured text provider returned an unexpected model identity.",
                false);

        stopwatch.Stop();
        _logger.LogInformation(
            "Structured text completion succeeded: Provider={ProviderName}, Model={ModelIdentifier}, DurationMs={DurationMs}",
            resolved.ProviderName,
            resolved.ModelIdentifier,
            stopwatch.ElapsedMilliseconds);
        return new StructuredTextCompletionResult(content, parsed.Model, parsed.Choices[0].FinishReason, stopwatch.Elapsed);
    }

    private HttpClient CreateClient(ResolvedModel resolved)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(resolved.ProviderBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(resolved.ProviderTimeoutSeconds);
        if (string.IsNullOrWhiteSpace(resolved.ApiKeyEncrypted))
            return client;

        try
        {
            var apiKey = _encryptionService.Decrypt(resolved.ApiKeyEncrypted);
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new StructuredTextCompletionException(
                    "structured_text_credential_empty",
                    $"Provider '{resolved.ProviderName}' inference credential is empty.",
                    false);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return client;
        }
        catch (StructuredTextCompletionException)
        {
            client.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            throw new StructuredTextCompletionException(
                "structured_text_credential_invalid",
                $"Provider '{resolved.ProviderName}' inference credential could not be decrypted.",
                false,
                ex);
        }
    }

    private static void ValidateRequest(
        ResolvedSceneBeatAnalyzer analyzer,
        StructuredTextCompletionRequest request)
    {
        if (analyzer.Model.IsSessionOverride)
            throw new StructuredTextCompletionException(
                "scene_beat_session_override_forbidden",
                "The scene-beat analyzer cannot use a session model override.",
                false);
        if (analyzer.Model.ThinkingMode == ThinkingMode.Default)
            throw new StructuredTextCompletionException(
                "scene_beat_thinking_mode_missing",
                "The scene-beat analyzer requires an explicit thinking mode.",
                false);
        if (string.IsNullOrWhiteSpace(request.SystemMessage) || string.IsNullOrWhiteSpace(request.UserMessage))
            throw new StructuredTextCompletionException(
                "structured_text_prompt_missing",
                "Structured text system and user messages are required.",
                false);
        if (string.IsNullOrWhiteSpace(request.ResponseSchemaName)
            || request.ResponseSchema.ValueKind != JsonValueKind.Object)
        {
            throw new StructuredTextCompletionException(
                "structured_text_schema_missing",
                "A named JSON object response schema is required.",
                false);
        }
    }

    private static string BuildJsonObjectSystemMessage(StructuredTextCompletionRequest request) =>
        $"""
        {request.SystemMessage}

        Return exactly one JSON object and no prose or Markdown. The JSON object must conform to this schema named '{request.ResponseSchemaName}':
        {request.ResponseSchema.GetRawText()}
        """;

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("top_p")] double TopP,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("response_format")] ResponseFormat ResponseFormat,
        [property: JsonPropertyName("chat_template_kwargs")] IReadOnlyDictionary<string, object> ChatTemplateKwargs);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ResponseFormat(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("json_schema"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonSchema? JsonSchema);

    private sealed record JsonSchema(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("strict")] bool Strict,
        [property: JsonPropertyName("schema")] JsonElement Schema);

    private sealed record ChatCompletionResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatResponseMessage? Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record ChatResponseMessage([property: JsonPropertyName("content")] string? Content);
}