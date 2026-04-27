using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

public sealed class CompletionClient : ICompletionClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<CompletionClient> _logger;

    public CompletionClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<CompletionClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<string> GenerateAsync(
        string prompt,
        ResolvedModel resolved,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage> { new("user", prompt) };
        return await SendCompletionAsync(messages, resolved, cancellationToken);
    }

    public async Task<string> GenerateAsync(
        string systemMessage,
        string userMessage,
        ResolvedModel resolved,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new("system", systemMessage),
            new("user", userMessage)
        };
        return await SendCompletionAsync(messages, resolved, cancellationToken);
    }

    public async Task<string> StreamGenerateAsync(
        string prompt,
        ResolvedModel resolved,
        Func<string, Task> onChunk,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage> { new("user", prompt) };
        return await SendCompletionStreamingAsync(messages, resolved, onChunk, cancellationToken);
    }

    public async Task<string> StreamGenerateAsync(
        string systemMessage,
        string userMessage,
        ResolvedModel resolved,
        Func<string, Task> onChunk,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new("system", systemMessage),
            new("user", userMessage)
        };

        return await SendCompletionStreamingAsync(messages, resolved, onChunk, cancellationToken);
    }

    public async Task<bool> CheckHealthAsync(
        string providerBaseUrl,
        int timeoutSeconds,
        string? decryptedApiKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            client.BaseAddress = new Uri(providerBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            if (!string.IsNullOrEmpty(decryptedApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedApiKey);
            }

            using var response = await client.GetAsync("/", cancellationToken);
            var isHealthy = response.IsSuccessStatusCode;
            _logger.LogInformation("Provider health check completed: {BaseUrl}, Status={StatusCode}", providerBaseUrl, (int)response.StatusCode);
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider health check failed: {BaseUrl}", providerBaseUrl);
            return false;
        }
    }

    public async Task<(bool Success, string Message)> CheckModelHealthAsync(
        string providerBaseUrl,
        string chatCompletionsPath,
        int timeoutSeconds,
        string? decryptedApiKey,
        string modelIdentifier,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            var baseUrl = providerBaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            if (!string.IsNullOrEmpty(decryptedApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedApiKey);
            }

            var payload = new ChatRequest
            {
                Model = modelIdentifier,
                Messages = [new("system", "Reply OK"), new("user", "test")],
                Temperature = 0.0,
                TopP = 1.0,
                MaxTokens = 1
            };

            var relativePath = chatCompletionsPath.TrimStart('/');
            using var response = await client.PostAsJsonAsync(relativePath, payload, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Model health check passed: {ModelIdentifier} at {BaseUrl}", modelIdentifier, providerBaseUrl);
                return (true, "Model is reachable and responding.");
            }

            var statusCode = (int)response.StatusCode;
            var errorMessage = statusCode switch
            {
                401 => "Invalid API key.",
                404 => $"Model '{modelIdentifier}' not found on provider.",
                429 => "Rate limit exceeded.",
                >= 500 => $"Provider server error ({statusCode}).",
                _ => $"Unexpected status {statusCode}."
            };

            _logger.LogWarning("Model health check failed: {ModelIdentifier} at {BaseUrl}, Status={StatusCode}", modelIdentifier, providerBaseUrl, statusCode);
            return (false, errorMessage);
        }
        catch (TaskCanceledException)
        {
            return (false, "Connection timed out.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Connection error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model health check failed: {ModelIdentifier} at {BaseUrl}", modelIdentifier, providerBaseUrl);
            return (false, $"Error: {ex.Message}");
        }
    }

    private async Task<string> SendCompletionAsync(
        List<ChatMessage> messages,
        ResolvedModel resolved,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        long setupMs = 0;
        long requestMs = 0;
        long readMs = 0;
        long parseMs = 0;
        long continuationMs = 0;
        var continuationCalls = 0;
        var finishReason = string.Empty;
        var choiceCount = 0;

        try
        {
            var setupStopwatch = Stopwatch.StartNew();
            var client = _httpClientFactory.CreateClient("CompletionClient");

            // Ensure BaseAddress ends with "/" so relative path resolution works correctly.
            // HttpClient resolves "v1/chat/completions" relative to "https://host/api/"
            // but "/v1/chat/completions" would reset to the root, ignoring the base path.
            var baseUrl = resolved.ProviderBaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(resolved.ProviderTimeoutSeconds);

            if (!string.IsNullOrEmpty(resolved.ApiKeyEncrypted))
            {
                try
                {
                    var decryptedKey = _encryptionService.Decrypt(resolved.ApiKeyEncrypted);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedKey);
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    _logger.LogError(ex, "Failed to decrypt API key for provider {ProviderName}. Please re-enter the API key in Model Manager.", resolved.ProviderName);
                    throw;
                }
            }

            var payload = new ChatRequest
            {
                Model = resolved.ModelIdentifier,
                Messages = messages,
                Temperature = resolved.Temperature,
                TopP = resolved.TopP,
                MaxTokens = resolved.MaxTokens
            };

            // Strip leading "/" from path so it resolves relative to BaseAddress, not root
            var relativePath = resolved.ChatCompletionsPath.TrimStart('/');
            setupStopwatch.Stop();
            setupMs = setupStopwatch.ElapsedMilliseconds;

            _logger.LogInformation(
                "Completion request start: Model={ModelIdentifier}, Provider={ProviderName}, SessionOverride={IsSessionOverride}, TimeoutSeconds={TimeoutSeconds}, MessageCount={MessageCount}, MessageChars={MessageChars}, SetupMs={SetupMs}",
                resolved.ModelIdentifier,
                resolved.ProviderName,
                resolved.IsSessionOverride,
                resolved.ProviderTimeoutSeconds,
                messages.Count,
                messages.Sum(x => x.Content?.Length ?? 0),
                setupMs);

            _logger.LogDebug("Sending completion request to {BaseUrl}{Path} with model {Model}",
                baseUrl, relativePath, resolved.ModelIdentifier);

            var requestStopwatch = Stopwatch.StartNew();
            using var response = await client.PostAsJsonAsync(relativePath, payload, cancellationToken);
            requestStopwatch.Stop();
            requestMs = requestStopwatch.ElapsedMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var statusCode = (int)response.StatusCode;

                var errorMessage = statusCode switch
                {
                    401 => $"Invalid API key for provider {resolved.ProviderName}",
                    429 => $"Rate limit exceeded for provider {resolved.ProviderName}",
                    >= 500 => $"Server error from provider {resolved.ProviderName}: {statusCode}",
                    _ => $"Request failed for provider {resolved.ProviderName}: {statusCode}"
                };

                _logger.LogError("Completion request failed: {ErrorMessage}, Response={ErrorContent}", errorMessage, errorContent);
                response.EnsureSuccessStatusCode();
            }

            var readStopwatch = Stopwatch.StartNew();
            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
            readStopwatch.Stop();
            readMs = readStopwatch.ElapsedMilliseconds;

            var parseStopwatch = Stopwatch.StartNew();
            var parsed = ParseContent(rawBody, resolved);
            parseStopwatch.Stop();
            parseMs = parseStopwatch.ElapsedMilliseconds;

            var content = parsed.Content;
            finishReason = parsed.FinishReason ?? string.Empty;
            choiceCount = parsed.ChoiceCount;

            if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning(
                    "Completion hit token limit, requesting continuation: Model={ModelIdentifier}, Provider={ProviderName}, MaxTokens={MaxTokens}",
                    resolved.ModelIdentifier,
                    resolved.ProviderName,
                    resolved.MaxTokens);

                var continuationStopwatch = Stopwatch.StartNew();
                var continuationResult = await ContinueTruncatedResponseAsync(
                    client,
                    relativePath,
                    messages,
                    resolved,
                    content,
                    cancellationToken);
                continuationStopwatch.Stop();

                continuationMs = continuationStopwatch.ElapsedMilliseconds;
                continuationCalls = continuationResult.CallCount;
                content = continuationResult.Content;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning(
                    "Completion returned empty content: Model={ModelIdentifier}, Provider={ProviderName}, FinishReason={FinishReason}, ChoicesCount={ChoicesCount}, RawLength={RawLength}, Body={RawBody}",
                    resolved.ModelIdentifier, resolved.ProviderName,
                    finishReason,
                    choiceCount,
                    rawBody.Length,
                    rawBody.Length > 2000 ? rawBody[..2000] : rawBody);
            }

            totalStopwatch.Stop();
            _logger.LogInformation(
                "Completion request completed: Model={ModelIdentifier}, Provider={ProviderName}, SessionOverride={IsSessionOverride}, TotalMs={TotalMs}, SetupMs={SetupMs}, HttpMs={HttpMs}, ReadMs={ReadMs}, ParseMs={ParseMs}, ContinuationMs={ContinuationMs}, ContinuationCalls={ContinuationCalls}, FinishReason={FinishReason}, ChoiceCount={ChoiceCount}",
                resolved.ModelIdentifier,
                resolved.ProviderName,
                resolved.IsSessionOverride,
                totalStopwatch.ElapsedMilliseconds,
                setupMs,
                requestMs,
                readMs,
                parseMs,
                continuationMs,
                continuationCalls,
                finishReason,
                choiceCount);

            return content ?? string.Empty;
        }
        catch (TaskCanceledException ex)
        {
            totalStopwatch.Stop();
            var cancelReason = cancellationToken.IsCancellationRequested ? "CallerCanceledToken" : "HttpTimeoutOrTransportCancel";
            _logger.LogWarning(
                ex,
                "Completion request canceled: Model={ModelIdentifier}, Provider={ProviderName}, Reason={CancelReason}, TimeoutSeconds={TimeoutSeconds}, ElapsedMs={ElapsedMs}, SetupMs={SetupMs}, HttpMs={HttpMs}, ReadMs={ReadMs}, ParseMs={ParseMs}",
                resolved.ModelIdentifier,
                resolved.ProviderName,
                cancelReason,
                resolved.ProviderTimeoutSeconds,
                totalStopwatch.ElapsedMilliseconds,
                setupMs,
                requestMs,
                readMs,
                parseMs);
            throw;
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            _logger.LogError(
                ex,
                "Completion request failed: Model={ModelIdentifier}, Provider={ProviderName}, TimeoutSeconds={TimeoutSeconds}, ElapsedMs={ElapsedMs}, SetupMs={SetupMs}, HttpMs={HttpMs}, ReadMs={ReadMs}, ParseMs={ParseMs}",
                resolved.ModelIdentifier,
                resolved.ProviderName,
                resolved.ProviderTimeoutSeconds,
                totalStopwatch.ElapsedMilliseconds,
                setupMs,
                requestMs,
                readMs,
                parseMs);
            throw;
        }
    }

    private async Task<string> SendCompletionStreamingAsync(
        List<ChatMessage> messages,
        ResolvedModel resolved,
        Func<string, Task> onChunk,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        var client = _httpClientFactory.CreateClient("CompletionClient");
        var baseUrl = resolved.ProviderBaseUrl.TrimEnd('/') + "/";
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(resolved.ProviderTimeoutSeconds);

        if (!string.IsNullOrEmpty(resolved.ApiKeyEncrypted))
        {
            try
            {
                var decryptedKey = _encryptionService.Decrypt(resolved.ApiKeyEncrypted);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedKey);
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                _logger.LogError(ex, "Failed to decrypt API key for provider {ProviderName}. Please re-enter the API key in Model Manager.", resolved.ProviderName);
                throw;
            }
        }

        var payload = new ChatRequest
        {
            Model = resolved.ModelIdentifier,
            Messages = messages,
            Temperature = resolved.Temperature,
            TopP = resolved.TopP,
            MaxTokens = resolved.MaxTokens,
            Stream = true
        };

        var relativePath = resolved.ChatCompletionsPath.TrimStart('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = (int)response.StatusCode;
            var errorMessage = statusCode switch
            {
                401 => $"Invalid API key for provider {resolved.ProviderName}",
                429 => $"Rate limit exceeded for provider {resolved.ProviderName}",
                >= 500 => $"Server error from provider {resolved.ProviderName}: {statusCode}",
                _ => $"Request failed for provider {resolved.ProviderName}: {statusCode}"
            };

            _logger.LogError("Streaming completion request failed: {ErrorMessage}, Response={ErrorContent}", errorMessage, errorContent);
            response.EnsureSuccessStatusCode();
        }

        var sb = new StringBuilder();
        string? finishReason = null;

        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        using (var reader = new StreamReader(stream))
        {
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var data = line[5..].Trim();
                if (data.Length == 0)
                {
                    continue;
                }

                if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
                {
                    break;
                }

                if (!TryParseStreamingChunk(data, out var chunkText, out var chunkFinishReason))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(chunkFinishReason))
                {
                    finishReason = chunkFinishReason;
                }

                if (string.IsNullOrEmpty(chunkText))
                {
                    continue;
                }

                sb.Append(chunkText);
                await onChunk(chunkText);
            }
        }

        var content = sb.ToString();
        if (string.IsNullOrWhiteSpace(content))
        {
            // Provider likely ignored stream=true and returned non-SSE JSON.
            content = await SendCompletionAsync(messages, resolved, cancellationToken);
            if (!string.IsNullOrWhiteSpace(content))
            {
                await onChunk(content);
            }
            finishReason = null;
        }
        else if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
        {
            var continuationResult = await ContinueTruncatedResponseAsync(
                client,
                relativePath,
                messages,
                resolved,
                content,
                cancellationToken);
            var continuation = continuationResult.Content;

            if (continuation.Length > content.Length)
            {
                var delta = continuation[content.Length..];
                if (!string.IsNullOrWhiteSpace(delta))
                {
                    await onChunk(delta);
                }
            }

            content = continuation;
        }

        var duration = DateTime.UtcNow - startTime;
        _logger.LogInformation(
            "Streaming completion request completed: Model={ModelIdentifier}, Provider={ProviderName}, SessionOverride={IsSessionOverride}, Duration={DurationMs}ms",
            resolved.ModelIdentifier, resolved.ProviderName, resolved.IsSessionOverride, (int)duration.TotalMilliseconds);

        return content;
    }

    private static bool TryParseStreamingChunk(string json, out string chunkText, out string? finishReason)
    {
        chunkText = string.Empty;
        finishReason = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                return false;
            }

            var firstChoice = choices[0];

            if (firstChoice.TryGetProperty("finish_reason", out var finishReasonElement)
                && finishReasonElement.ValueKind == JsonValueKind.String)
            {
                finishReason = finishReasonElement.GetString();
            }

            if (firstChoice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
            {
                if (delta.TryGetProperty("content", out var deltaContent) && deltaContent.ValueKind == JsonValueKind.String)
                {
                    chunkText = deltaContent.GetString() ?? string.Empty;
                    return true;
                }

                if (delta.TryGetProperty("reasoning_content", out var deltaReasoning) && deltaReasoning.ValueKind == JsonValueKind.String)
                {
                    chunkText = deltaReasoning.GetString() ?? string.Empty;
                    return true;
                }

                if (delta.TryGetProperty("reasoning", out var deltaReasoningFallback) && deltaReasoningFallback.ValueKind == JsonValueKind.String)
                {
                    chunkText = deltaReasoningFallback.GetString() ?? string.Empty;
                    return true;
                }
            }

            if (firstChoice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
            {
                if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    chunkText = content.GetString() ?? string.Empty;
                    return true;
                }

                if (message.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
                {
                    chunkText = reasoning.GetString() ?? string.Empty;
                    return true;
                }

                if (message.TryGetProperty("reasoning", out var reasoningFallback) && reasoningFallback.ValueKind == JsonValueKind.String)
                {
                    chunkText = reasoningFallback.GetString() ?? string.Empty;
                    return true;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(string Content, int CallCount)> ContinueTruncatedResponseAsync(
        HttpClient client,
        string relativePath,
        List<ChatMessage> originalMessages,
        ResolvedModel resolved,
        string initialContent,
        CancellationToken cancellationToken)
    {
        const int maxContinuationCalls = 2;
        var accumulated = initialContent;
        var callCount = 0;

        for (var i = 0; i < maxContinuationCalls; i++)
        {
            var continuationMessages = new List<ChatMessage>(originalMessages)
            {
                new("assistant", accumulated),
                new("user", "Continue exactly where you left off. Do not repeat previous text. Finish the remaining answer.")
            };

            var continuationPayload = new ChatRequest
            {
                Model = resolved.ModelIdentifier,
                Messages = continuationMessages,
                Temperature = resolved.Temperature,
                TopP = resolved.TopP,
                MaxTokens = resolved.MaxTokens
            };

            var continuationCallStopwatch = Stopwatch.StartNew();
            using var continuationResponse = await client.PostAsJsonAsync(relativePath, continuationPayload, cancellationToken);
            continuationCallStopwatch.Stop();
            callCount++;
            if (!continuationResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Continuation call failed: Model={ModelIdentifier}, Provider={ProviderName}, Status={StatusCode}, CallIndex={CallIndex}, DurationMs={DurationMs}",
                    resolved.ModelIdentifier,
                    resolved.ProviderName,
                    (int)continuationResponse.StatusCode,
                    i + 1,
                    continuationCallStopwatch.ElapsedMilliseconds);
                break;
            }

            var continuationRaw = await continuationResponse.Content.ReadAsStringAsync(cancellationToken);
            var (segment, finishReason, _) = ParseContent(continuationRaw, resolved);

            _logger.LogInformation(
                "Continuation call completed: Model={ModelIdentifier}, Provider={ProviderName}, CallIndex={CallIndex}, DurationMs={DurationMs}, SegmentLength={SegmentLength}, FinishReason={FinishReason}",
                resolved.ModelIdentifier,
                resolved.ProviderName,
                i + 1,
                continuationCallStopwatch.ElapsedMilliseconds,
                segment?.Length ?? 0,
                finishReason ?? string.Empty);

            if (string.IsNullOrWhiteSpace(segment))
            {
                break;
            }

            accumulated = string.Concat(accumulated.TrimEnd(), "\n", segment.TrimStart());

            if (!string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return (accumulated, callCount);
    }

    private (string? Content, string? FinishReason, int ChoiceCount) ParseContent(string rawBody, ResolvedModel resolved)
    {
        var result = System.Text.Json.JsonSerializer.Deserialize<ChatResponse>(rawBody);
        var firstChoice = result?.Choices?.FirstOrDefault();
        var content = firstChoice?.Message?.Content;

        if (string.IsNullOrWhiteSpace(content))
        {
            var fallback = firstChoice?.Message?.ReasoningContent ?? firstChoice?.Message?.Reasoning;
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                content = fallback;
                _logger.LogInformation(
                    "Using reasoning field as fallback: Model={ModelIdentifier}, Provider={ProviderName}",
                    resolved.ModelIdentifier,
                    resolved.ProviderName);
            }
        }

        return (content, firstChoice?.FinishReason, result?.Choices?.Count ?? 0);
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; init; } = [];

        [JsonPropertyName("temperature")]
        public double Temperature { get; init; } = 0.7;

        [JsonPropertyName("top_p")]
        public double TopP { get; init; } = 0.9;

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; init; } = 500;

        [JsonPropertyName("stream")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Stream { get; init; }
    }

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content);

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; init; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public ChatMessageResponse? Message { get; init; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; init; }
    }

    private sealed class ChatMessageResponse
    {
        [JsonPropertyName("role")]
        public string? Role { get; init; }

        [JsonPropertyName("content")]
        public string? Content { get; init; }

        [JsonPropertyName("reasoning_content")]
        public string? ReasoningContent { get; init; }

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; init; }
    }
}
