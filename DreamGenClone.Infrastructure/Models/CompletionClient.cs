using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Globalization;
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

    public async Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(
        string prompt,
        ResolvedModel resolved,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage> { new("user", prompt) };
        return await SendCompletionWithReasoningAsync(messages, resolved, cancellationToken);
    }

    public async Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(
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
        return await SendCompletionWithReasoningAsync(messages, resolved, cancellationToken);
    }

    public async Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(
        string prompt,
        ResolvedModel resolved,
        Func<string, Task> onChunk,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage> { new("user", prompt) };
        return await SendCompletionStreamingWithReasoningAsync(messages, resolved, onChunk, cancellationToken);
    }

    public async Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(
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
        return await SendCompletionStreamingWithReasoningAsync(messages, resolved, onChunk, cancellationToken);
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
            client.BaseAddress = new Uri(providerBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            if (!string.IsNullOrEmpty(decryptedApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedApiKey);
            }

            // A model listing is free and still reports 401/403 for a bad key, so it replaces any call that
            // would bill the account. Health checks never request a completion.
            using (var listing = await client.GetAsync("v1/models", cancellationToken))
            {
                if (listing.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Provider health check passed (model list, no completion requested): {BaseUrl}", providerBaseUrl);
                    return true;
                }

                var listingStatus = (int)listing.StatusCode;
                if (listingStatus is 401 or 403)
                {
                    _logger.LogWarning("Provider health check failed: key rejected at {BaseUrl}, Status={StatusCode}", providerBaseUrl, listingStatus);
                    return false;
                }
            }

            // Providers without a listing route still get a plain reachability ping.
            using var response = await client.GetAsync(string.Empty, cancellationToken);
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

            // Free model listing only. A 1-token completion would prove more, but it bills the account, so the
            // check stops here and says what it could not verify.
            var probe = await ProviderModelsProbe.QueryAsync(client, chatCompletionsPath, modelIdentifier, cancellationToken);

            switch (probe.StatusCode)
            {
                case 401 or 403:
                    _logger.LogWarning("Model health check failed: key rejected for {ModelIdentifier} at {BaseUrl}, Status={StatusCode}", modelIdentifier, providerBaseUrl, probe.StatusCode);
                    return (false, $"API key rejected (HTTP {probe.StatusCode}).");

                case 402:
                    return (false, "Payment required — the provider account has no credit.");

                case 429:
                    return (false, "Rate limit exceeded while listing models.");

                case >= 500:
                    return (false, $"Provider server error ({probe.StatusCode}).");
            }

            if (!probe.ListingSupported)
            {
                using var reach = await client.GetAsync(string.Empty, cancellationToken);
                if (!reach.IsSuccessStatusCode)
                {
                    return (false, $"Provider returned HTTP {(int)reach.StatusCode} and exposes no model list (HTTP {probe.StatusCode}) to check the key against.");
                }

                _logger.LogInformation("Model health check: {ModelIdentifier} at {BaseUrl} exposes no model list; reachability only, no completion requested", modelIdentifier, providerBaseUrl);
                return (true, $"Provider is reachable, but it exposes no model list to check the key against. "
                    + "No completion was requested, so the key stays unverified until the first real call.");
            }

            if (probe.StatusCode >= 400)
            {
                return (false, $"Model list request failed (HTTP {probe.StatusCode}).");
            }

            if (!probe.ContainsModel)
            {
                _logger.LogWarning("Model health check: {ModelIdentifier} is not listed at {BaseUrl}", modelIdentifier, providerBaseUrl);
                return (false, $"API key accepted, but '{modelIdentifier}' is not in the provider's model list.");
            }

            _logger.LogInformation("Model health check passed (model list, no completion requested): {ModelIdentifier} at {BaseUrl}", modelIdentifier, providerBaseUrl);
            return (true, $"API key accepted and '{modelIdentifier}' is listed by the provider. No completion was requested.");
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
        CancellationToken cancellationToken,
        bool allowParameterRetry = true)
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
                MaxTokens = resolved.MaxTokens,
                PresencePenalty = resolved.PresencePenalty,
                Seed = resolved.Seed,
                ChatTemplateKwargs = UsesChatTemplateThinking(resolved)
                    ? new Dictionary<string, object> { ["thinking"] = resolved.ThinkingMode == ThinkingMode.Enabled }
                    : null,
                Thinking = DeepSeekThinking(resolved)
            };

            _logger.LogDebug(
                "Completion request built: Model={ModelIdentifier}, SupportsThinkingControl={SupportsThinkingControl}, ThinkingMode={ThinkingMode}",
                resolved.ModelIdentifier,
                resolved.SupportsThinkingControl,
                resolved.ThinkingMode);

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

                // Some hosted models refuse non-streaming requests outright (Together's Qwen3.7/3.8 family
                // answers 400 streaming_required). Retry the same messages over the streaming path, which
                // yields one assembled string, so callers such as Prompt Tester work with every model.
                if (statusCode == 400 && IsStreamingRequired(errorContent))
                {
                    _logger.LogInformation(
                        "Model requires streaming; retrying with stream=true: Model={ModelIdentifier}, Provider={ProviderName}",
                        resolved.ModelIdentifier,
                        resolved.ProviderName);
                    return await SendCompletionStreamingAsync(messages, resolved, _ => Task.CompletedTask, cancellationToken, allowNonStreamingFallback: false);
                }

                // A provider may reject a parameter the app sends by default (Together: "top_p must be
                // within [0.95, 1]"). Clamp to the range the provider itself states and retry once.
                if (statusCode == 400 && allowParameterRetry && TryClampRejectedParameter(errorContent, resolved) is { } adjusted)
                {
                    _logger.LogInformation(
                        "Parameter rejected; retrying with TopP={TopP}, Temperature={Temperature}, MaxTokens={MaxTokens}: Model={ModelIdentifier}",
                        adjusted.TopP,
                        adjusted.Temperature,
                        adjusted.MaxTokens,
                        resolved.ModelIdentifier);
                    return await SendCompletionAsync(messages, adjusted, cancellationToken, allowParameterRetry: false);
                }

                var errorMessage = statusCode switch
                {
                    401 => $"Invalid API key for provider {resolved.ProviderName}",
                    429 => $"Rate limit exceeded for provider {resolved.ProviderName}",
                    >= 500 => $"Server error from provider {resolved.ProviderName}: {statusCode}",
                    _ => $"Request failed for provider {resolved.ProviderName}: {statusCode}"
                };

                // Carry the provider's own explanation into the exception so the UI says why a request was
                // rejected ("This model only supports streaming", "Unable to access non-serverless model"),
                // instead of a bare 400.
                var providerDetail = TryReadProviderMessage(errorContent);
                if (!string.IsNullOrWhiteSpace(providerDetail))
                {
                    errorMessage += $" — {providerDetail}";
                }

                _logger.LogError("Completion request failed: {ErrorMessage}, Response={ErrorContent}", errorMessage, errorContent);
                throw new HttpRequestException(errorMessage, inner: null, response.StatusCode);
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
        CancellationToken cancellationToken,
        bool allowNonStreamingFallback = true)
    {
        // Wrap the chunk callback so that ObjectDisposedException / InvalidOperationException
        // (thrown when the Blazor component that owns the callback has been disposed due to
        // page navigation) never fault the engine task.  The wrapper is a no-op after the
        // first disposal exception, mirroring RolePlayChunkCallbackWrapper behaviour.
        var safeOnChunk = onChunk;
        onChunk = async chunk =>
        {
            try
            {
                await safeOnChunk(chunk).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        };

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
            PresencePenalty = resolved.PresencePenalty,
            Seed = resolved.Seed,
            Stream = true,
            ChatTemplateKwargs = UsesChatTemplateThinking(resolved)
                ? new Dictionary<string, object> { ["thinking"] = resolved.ThinkingMode == ThinkingMode.Enabled }
                : null,
            Thinking = DeepSeekThinking(resolved)
        };

        var relativePath = resolved.ChatCompletionsPath.TrimStart('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var statusCode = (int)response.StatusCode;
            var errorMessage = statusCode switch
            {
                401 => $"Invalid API key for provider {resolved.ProviderName}",
                429 => $"Rate limit exceeded for provider {resolved.ProviderName}",
                >= 500 => $"Server error from provider {resolved.ProviderName}: {statusCode}",
                _ => $"Request failed for provider {resolved.ProviderName}: {statusCode}"
            };

            var providerDetail = TryReadProviderMessage(errorContent);
            if (!string.IsNullOrWhiteSpace(providerDetail))
            {
                errorMessage += $" — {providerDetail}";
            }

            _logger.LogError("Streaming completion request failed: {ErrorMessage}, Response={ErrorContent}", errorMessage, errorContent);
            throw new HttpRequestException(errorMessage, inner: null, response.StatusCode);
        }

        var sb = new StringBuilder();
        string? finishReason = null;
        string? streamError = null;
        var sawDone = false;

        // IMPORTANT (UI responsiveness):
        //   1. Do NOT use `reader.EndOfStream` in the loop condition. That property performs a
        //      synchronous, blocking read on the underlying network stream whenever the buffer
        //      is empty. When this method is awaited from the Blazor renderer's synchronization
        //      context, those blocking reads run on the UI dispatcher thread and freeze the UI
        //      between SSE chunks (very visible on slow models).
        //   2. ConfigureAwait(false) on every await keeps continuations on the thread-pool
        //      instead of bouncing back to the renderer dispatcher, so other UI events
        //      (e.g. adaptive-panel clicks/scroll) can be processed while the stream is active.
        //   3. `await onChunk(...).ConfigureAwait(false)` ensures the chunk callback's
        //      completion does not drag us back onto the renderer dispatcher either; the
        //      callback itself is responsible for marshalling any UI updates via InvokeAsync.
        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        using (var reader = new StreamReader(stream))
        {
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                    sawDone = true;
                    break;
                }

                // Together's managed chat layer ends an aborted generation with a bare error event
                // ({"error":{"code":"managed_chat_error",...}}) and no [DONE] and no finish_reason.
                // Ignoring it silently returned a half-written scene as if it were a complete answer.
                if (data.Contains("\"error\"", StringComparison.Ordinal))
                {
                    streamError = TryReadProviderMessage(data) ?? "The provider aborted the stream.";
                    break;
                }

                if (!TryParseStreamingChunkWithKind(data, out var chunkText, out var isReasoning, out var chunkFinishReason))
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

                if (isReasoning)
                {
                    sb.Append(chunkText);
                }
                else
                {
                    sb.Append(chunkText);
                    await onChunk(chunkText).ConfigureAwait(false);
                }
            }
        }

        var content = sb.ToString();

        if (streamError is not null)
        {
            _logger.LogError(
                "Streaming completion aborted by provider: {Error}, PartialChars={Chars}, Model={Model}",
                streamError,
                content.Length,
                resolved.ModelIdentifier);
            throw new IncompleteCompletionException(streamError, content, partialReasoning: null, finishReason);
        }

        if (!sawDone && finishReason is null)
        {
            _logger.LogWarning(
                "Streaming completion ended without [DONE] or finish_reason: Model={Model}, Chars={Chars}",
                resolved.ModelIdentifier,
                content.Length);
        }

        if (string.IsNullOrWhiteSpace(content) && allowNonStreamingFallback)
        {
            // Provider likely ignored stream=true and returned non-SSE JSON. Skipped when this call was itself
            // the streaming fallback for a stream-only model, which would otherwise recurse forever.
            content = await SendCompletionAsync(messages, resolved, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(content))
            {
                await onChunk(content).ConfigureAwait(false);
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
                cancellationToken).ConfigureAwait(false);
            var continuation = continuationResult.Content;

            if (continuation.Length > content.Length)
            {
                var delta = continuation[content.Length..];
                if (!string.IsNullOrWhiteSpace(delta))
                {
                    await onChunk(delta).ConfigureAwait(false);
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

    private async Task<(string Content, string? Reasoning)> SendCompletionWithReasoningAsync(
        List<ChatMessage> messages,
        ResolvedModel resolved,
        CancellationToken cancellationToken,
        bool allowParameterRetry = true)
    {
        var totalStopwatch = Stopwatch.StartNew();

        try
        {
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
                    _logger.LogError(ex, "Failed to decrypt API key for provider {ProviderName}", resolved.ProviderName);
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
                PresencePenalty = resolved.PresencePenalty,
                Seed = resolved.Seed,
                ChatTemplateKwargs = UsesChatTemplateThinking(resolved)
                    ? new Dictionary<string, object> { ["thinking"] = resolved.ThinkingMode == ThinkingMode.Enabled }
                    : null,
                Thinking = DeepSeekThinking(resolved)
            };

            var relativePath = resolved.ChatCompletionsPath.TrimStart('/');
            using var response = await client.PostAsJsonAsync(relativePath, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var statusCode = (int)response.StatusCode;

                // Same recoverable 400s as the plain path: stream-only models and rejected parameter ranges.
                if (statusCode == 400 && IsStreamingRequired(errorContent))
                {
                    _logger.LogInformation(
                        "Model requires streaming; retrying reasoning-aware call with stream=true: Model={ModelIdentifier}",
                        resolved.ModelIdentifier);
                    return await SendCompletionStreamingWithReasoningAsync(
                        messages, resolved, _ => Task.CompletedTask, cancellationToken, allowNonStreamingFallback: false);
                }

                if (statusCode == 400 && TryClampRejectedParameter(errorContent, resolved) is { } adjusted)
                {
                    _logger.LogInformation(
                        "Parameter rejected; retrying reasoning-aware call with TopP={TopP}, MaxTokens={MaxTokens}: Model={ModelIdentifier}",
                        adjusted.TopP,
                        adjusted.MaxTokens,
                        resolved.ModelIdentifier);
                    return await SendCompletionWithReasoningAsync(messages, adjusted, cancellationToken, allowParameterRetry: false);
                }

                _logger.LogError("Completion (reasoning-aware) failed: {Status}, {Error}", statusCode, errorContent);
                var detail = TryReadProviderMessage(errorContent);
                var message = $"Request failed for provider {resolved.ProviderName}: {statusCode}";
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    message += $" — {detail}";
                }

                throw new HttpRequestException(message, inner: null, response.StatusCode);
            }

            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var (content, reasoning, finishReason, _) = ParseContentWithReasoning(rawBody, resolved);

            if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(content))
            {
                var continuationResult = await ContinueTruncatedResponseAsync(
                    client, relativePath, messages, resolved, content, cancellationToken);
                content = continuationResult.Content;
            }
            else if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase)
                     && string.IsNullOrWhiteSpace(content)
                     && !string.IsNullOrWhiteSpace(reasoning))
            {
                // Reasoning models (e.g. DeepSeek) can spend the entire token budget on
                // reasoning_content and emit zero content, hitting finish_reason=length before
                // reaching the answer. Send one focused follow-up that asks for ONLY the final
                // answer, so the model doesn't have to re-reason from scratch.
                content = await ForceAnswerFromReasoningAsync(
                    client, relativePath, messages, resolved, reasoning, cancellationToken);
            }

            totalStopwatch.Stop();
            _logger.LogInformation(
                "Completion (reasoning-aware) done: Model={Model}, ContentLen={ContentLen}, ReasoningLen={ReasoningLen}, DurationMs={DurationMs}",
                resolved.ModelIdentifier, content?.Length ?? 0, reasoning?.Length ?? 0, totalStopwatch.ElapsedMilliseconds);

            return (content ?? string.Empty, reasoning);
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            _logger.LogError(ex, "Completion (reasoning-aware) failed: Model={Model}", resolved.ModelIdentifier);
            throw;
        }
    }

    private async Task<(string Content, string? Reasoning)> SendCompletionStreamingWithReasoningAsync(
        List<ChatMessage> messages,
        ResolvedModel resolved,
        Func<string, Task> onChunk,
        CancellationToken cancellationToken,
        bool allowNonStreamingFallback = true)
    {
        // Wrap the chunk callback for disposal safety
        var safeOnChunk = onChunk;
        onChunk = async chunk =>
        {
            try { await safeOnChunk(chunk).ConfigureAwait(false); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        };

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
                _logger.LogError(ex, "Failed to decrypt API key for provider {ProviderName}", resolved.ProviderName);
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
            PresencePenalty = resolved.PresencePenalty,
            Seed = resolved.Seed,
            Stream = true,
            ChatTemplateKwargs = UsesChatTemplateThinking(resolved)
                ? new Dictionary<string, object> { ["thinking"] = resolved.ThinkingMode == ThinkingMode.Enabled }
                : null,
            Thinking = DeepSeekThinking(resolved)
        };

        var relativePath = resolved.ChatCompletionsPath.TrimStart('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("Streaming completion (reasoning-aware) failed: {StatusCode}, {Error}", (int)response.StatusCode, errorContent);
            response.EnsureSuccessStatusCode();
        }

        var contentSb = new StringBuilder();
        var reasoningSb = new StringBuilder();
        string? finishReason = null;
        string? streamError = null;
        var sawDone = false;

        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        using (var reader = new StreamReader(stream))
        {
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

                var data = line[5..].Trim();
                if (data.Length == 0) continue;
                if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
                {
                    sawDone = true;
                    break;
                }

                // Same abort contract as the plain path: an error event with no [DONE] and no finish_reason.
                if (data.Contains("\"error\"", StringComparison.Ordinal))
                {
                    streamError = TryReadProviderMessage(data) ?? "The provider aborted the stream.";
                    break;
                }

                if (!TryParseStreamingChunkWithKind(data, out var chunkText, out var isReasoning, out var chunkFinishReason))
                    continue;

                if (!string.IsNullOrWhiteSpace(chunkFinishReason))
                    finishReason = chunkFinishReason;

                if (string.IsNullOrEmpty(chunkText)) continue;

                if (isReasoning)
                    reasoningSb.Append(chunkText);
                else
                {
                    contentSb.Append(chunkText);
                    await onChunk(chunkText).ConfigureAwait(false);
                }
            }
        }

        var content = contentSb.ToString();
        var reasoning = reasoningSb.Length > 0 ? reasoningSb.ToString() : null;

        if (streamError is not null)
        {
            _logger.LogError(
                "Streaming completion (reasoning-aware) aborted by provider: {Error}, PartialChars={Chars}, ReasoningChars={ReasoningChars}, Model={Model}",
                streamError,
                content.Length,
                reasoning?.Length ?? 0,
                resolved.ModelIdentifier);
            throw new IncompleteCompletionException(streamError, content, reasoning, finishReason);
        }

        if (!sawDone && finishReason is null)
        {
            _logger.LogWarning(
                "Streaming completion (reasoning-aware) ended without [DONE] or finish_reason: Model={Model}, Chars={Chars}",
                resolved.ModelIdentifier,
                content.Length);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            // Reasoning-only response (empty content): try a focused force-answer follow-up when the
            // model reasoned but never emitted content; otherwise fall back to the plain path.
            if (!string.IsNullOrWhiteSpace(reasoning))
            {
                content = await ForceAnswerFromReasoningAsync(
                    client, relativePath, messages, resolved, reasoning, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Skipped when this call was itself the streaming fallback for a stream-only model, which
                // would otherwise bounce back through the non-streaming path forever.
                content = allowNonStreamingFallback
                    ? await SendCompletionAsync(messages, resolved, cancellationToken).ConfigureAwait(false)
                    : string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(content))
                await onChunk(content).ConfigureAwait(false);
        }
        else if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
        {
            var continuationResult = await ContinueTruncatedResponseAsync(
                client, relativePath, messages, resolved, content, cancellationToken).ConfigureAwait(false);
            var continuation = continuationResult.Content;
            if (continuation.Length > content.Length)
            {
                var delta = continuation[content.Length..];
                if (!string.IsNullOrWhiteSpace(delta))
                    await onChunk(delta).ConfigureAwait(false);
            }
            content = continuation;
        }

        var duration = DateTime.UtcNow - startTime;
        _logger.LogInformation(
            "Streaming completion (reasoning-aware) done: Model={Model}, ContentLen={ContentLen}, ReasoningLen={ReasoningLen}, Duration={DurationMs}ms",
            resolved.ModelIdentifier, content.Length, reasoning?.Length ?? 0, (int)duration.TotalMilliseconds);

        return (content, reasoning);
    }

    /// <summary>
    /// Parses a streaming SSE chunk and extracts only the content text.
    /// Reasoning/thinking chunks are silently skipped to prevent them from
    /// leaking into the interaction text.
    /// </summary>
    private static bool TryParseStreamingChunk(string json, out string chunkText, out string? finishReason)
    {
        var result = TryParseStreamingChunkWithKind(json, out chunkText, out var isReasoning, out finishReason);
        if (result && isReasoning)
        {
            chunkText = string.Empty;
        }
        return result;
    }

    /// <summary>
    /// Parses a streaming SSE chunk and indicates whether the chunk is model reasoning
    /// (chain-of-thought) or actual content.
    /// </summary>
    private static bool TryParseStreamingChunkWithKind(string json, out string chunkText, out bool isReasoning, out string? finishReason)
    {
        chunkText = string.Empty;
        isReasoning = false;
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
                    isReasoning = true;
                    return true;
                }

                if (delta.TryGetProperty("reasoning", out var deltaReasoningFallback) && deltaReasoningFallback.ValueKind == JsonValueKind.String)
                {
                    chunkText = deltaReasoningFallback.GetString() ?? string.Empty;
                    isReasoning = true;
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
                    isReasoning = true;
                    return true;
                }

                if (message.TryGetProperty("reasoning", out var reasoningFallback) && reasoningFallback.ValueKind == JsonValueKind.String)
                {
                    chunkText = reasoningFallback.GetString() ?? string.Empty;
                    isReasoning = true;
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

    /// <summary>
    /// Focused follow-up for reasoning models (e.g. DeepSeek) that spent the whole token budget
    /// on reasoning_content and hit finish_reason=length with empty content. Asks the model to
    /// output ONLY the final answer based on the reasoning it already produced, without re-reasoning.
    /// Returns the (possibly empty) content from the follow-up call.
    /// </summary>
    private async Task<string> ForceAnswerFromReasoningAsync(
        HttpClient client,
        string relativePath,
        List<ChatMessage> originalMessages,
        ResolvedModel resolved,
        string reasoning,
        CancellationToken cancellationToken)
    {
        const string forceAnswerPrompt =
            "You already analyzed the task above. Output ONLY the final answer now — no more reasoning, no explanation. " +
            "Respond in the exact format the original task requested.";

        // Trim very long reasoning so it fits within context: keep the tail (most recent analysis).
        const int maxReasoningChars = 8000;
        var trimmedReasoning = reasoning.Length > maxReasoningChars
            ? "…(earlier reasoning trimmed)… " + reasoning[^maxReasoningChars..]
            : reasoning;

        var forceMessages = new List<ChatMessage>(originalMessages)
        {
            new("assistant", trimmedReasoning),
            new("user", forceAnswerPrompt)
        };

        var payload = new ChatRequest
        {
            Model = resolved.ModelIdentifier,
            Messages = forceMessages,
            Temperature = 0,
            TopP = resolved.TopP,
            MaxTokens = resolved.MaxTokens,
            PresencePenalty = resolved.PresencePenalty,
            Seed = resolved.Seed,
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var forceResponse = await client.PostAsJsonAsync(relativePath, payload, cancellationToken);
            stopwatch.Stop();

            if (!forceResponse.IsSuccessStatusCode)
            {
                var errorContent = await forceResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Force-answer call failed: Model={ModelIdentifier}, Provider={ProviderName}, Status={StatusCode}, DurationMs={DurationMs}, Error={Error}",
                    resolved.ModelIdentifier, resolved.ProviderName, (int)forceResponse.StatusCode, stopwatch.ElapsedMilliseconds, errorContent);
                return string.Empty;
            }

            var forceRaw = await forceResponse.Content.ReadAsStringAsync(cancellationToken);
            var (forceContent, forceReasoningExtracted, forceFinishReason, _) = ParseContentWithReasoning(forceRaw, resolved);

            // If the follow-up ALSO hit length with empty content, return empty so callers fail
            // explicitly rather than receive reasoning text as if it were content.
            if (string.IsNullOrWhiteSpace(forceContent)
                && string.Equals(forceFinishReason, "length", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(forceReasoningExtracted))
            {
                _logger.LogWarning(
                    "Force-answer call still empty after length: Model={ModelIdentifier}, ReasoningLen={ReasoningLen}, DurationMs={DurationMs}",
                    resolved.ModelIdentifier, forceReasoningExtracted?.Length ?? 0, stopwatch.ElapsedMilliseconds);
                return string.Empty;
            }

            // If the follow-up produced content but STILL hit the token ceiling (e.g. a truncated
            // structured JSON answer), attempt the standard continuation so the remaining answer
            // has a chance to complete. Mirrors the main reasoning-aware path's length handling.
            if (!string.IsNullOrWhiteSpace(forceContent)
                && string.Equals(forceFinishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                var continuationResult = await ContinueTruncatedResponseAsync(
                    client, relativePath, originalMessages, resolved, forceContent, cancellationToken);
                var continuationContent = continuationResult.Content;
                _logger.LogWarning(
                    "Force-answer call hit length with content; continuation attempted: Model={ModelIdentifier}, ContentLen={ContentLen}, ContinuationCalls={ContinuationCalls}, ContinuationContentLen={ContinuationContentLen}",
                    resolved.ModelIdentifier, continuationContent.Length, continuationResult.CallCount, continuationContent.Length);
                if (continuationContent.Length > forceContent.Length)
                {
                    forceContent = continuationContent;
                }
            }

            _logger.LogInformation(
                "Force-answer call completed: Model={ModelIdentifier}, Provider={ProviderName}, ContentLen={ContentLen}, ReasoningLen={ReasoningLen}, FinishReason={FinishReason}, DurationMs={DurationMs}",
                resolved.ModelIdentifier, resolved.ProviderName, forceContent?.Length ?? 0, forceReasoningExtracted?.Length ?? 0, forceFinishReason, stopwatch.ElapsedMilliseconds);

            return forceContent ?? string.Empty;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex,
                "Force-answer call exception: Model={ModelIdentifier}, Provider={ProviderName}, DurationMs={DurationMs}",
                resolved.ModelIdentifier, resolved.ProviderName, stopwatch.ElapsedMilliseconds);
            return string.Empty;
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
                MaxTokens = resolved.MaxTokens,
                PresencePenalty = resolved.PresencePenalty,
                Seed = resolved.Seed,
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

        return (content, firstChoice?.FinishReason, result?.Choices?.Count ?? 0);
    }

    /// <summary>
    /// Parses a non-streaming completion response, extracting both the content text
    /// and any model reasoning (reasoning_content / reasoning) as separate values.
    /// </summary>
    private (string? Content, string? Reasoning, string? FinishReason, int ChoiceCount) ParseContentWithReasoning(
        string rawBody, ResolvedModel resolved)
    {
        var result = System.Text.Json.JsonSerializer.Deserialize<ChatResponse>(rawBody);
        var firstChoice = result?.Choices?.FirstOrDefault();
        var content = firstChoice?.Message?.Content;
        var reasoning = firstChoice?.Message?.ReasoningContent ?? firstChoice?.Message?.Reasoning;

        if (!string.IsNullOrWhiteSpace(reasoning))
        {
            _logger.LogInformation(
                "Extracted reasoning from response: Model={Model}, ReasoningLen={ReasoningLen}",
                resolved.ModelIdentifier, reasoning.Length);
        }

        return (content, reasoning, firstChoice?.FinishReason, result?.Choices?.Count ?? 0);
    }

    /// <summary>
    /// DeepSeek's own API toggles thinking with a top-level <c>thinking: {type}</c> object, not with
    /// <c>chat_template_kwargs</c>; sending the Qwen/GLM shape there is silently ignored.
    /// </summary>
    private static bool UsesDeepSeekThinkingApi(ResolvedModel resolved)
        => resolved.ProviderBaseUrl.Contains("api.deepseek.com", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>chat_template_kwargs.thinking</c> for providers that accept it.</summary>
    private static bool UsesChatTemplateThinking(ResolvedModel resolved)
        => !UsesDeepSeekThinkingApi(resolved)
           && resolved.SupportsThinkingControl
           && resolved.ThinkingMode != ThinkingMode.Default;

    /// <summary><c>thinking: {type}</c> for DeepSeek, null when the model has no thinking control.</summary>
    private static Dictionary<string, string>? DeepSeekThinking(ResolvedModel resolved)
        => UsesDeepSeekThinkingApi(resolved)
           && resolved.SupportsThinkingControl
           && resolved.ThinkingMode != ThinkingMode.Default
            ? new Dictionary<string, string>
            {
                ["type"] = resolved.ThinkingMode == ThinkingMode.Enabled ? "enabled" : "disabled"
            }
            : null;

    /// <summary>True when the provider rejected a non-streaming request because the model is stream-only.</summary>
    private static bool IsStreamingRequired(string errorContent)
        => errorContent.Contains("only supports streaming", StringComparison.OrdinalIgnoreCase)
           || errorContent.Contains("streaming_required", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a provider parameter rejection of the shape
    /// <c>{"error":{"code":"invalid_top_p","message":"top_p must be within [0.95, 1], got 0.9"}}</c>
    /// and returns the model with that one parameter clamped into the stated range. Returns null when the
    /// error is not a range rejection, or when the value already sits inside the range.
    /// </summary>
    private static ResolvedModel? TryClampRejectedParameter(string errorContent, ResolvedModel resolved)
    {
        string? code;
        string? parameter;
        string? message;
        try
        {
            using var document = JsonDocument.Parse(errorContent);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            code = ReadString(error, "code");
            parameter = ReadString(error, "param");
            message = ReadString(error, "message");
        }
        catch (JsonException)
        {
            return null;
        }

        var subject = (code ?? parameter ?? message ?? string.Empty).ToLowerInvariant();
        var range = ExtractRange(message ?? string.Empty);
        if (subject.Length == 0 || range is null)
        {
            return null;
        }

        var (min, max) = range.Value;

        if (subject.Contains("top_p"))
        {
            var clamped = Clamp(resolved.TopP, min, max);
            return Math.Abs(clamped - resolved.TopP) < 1e-9 ? null : resolved with { TopP = clamped };
        }

        if (subject.Contains("temperature"))
        {
            var clamped = Clamp(resolved.Temperature, min, max);
            return Math.Abs(clamped - resolved.Temperature) < 1e-9 ? null : resolved with { Temperature = clamped };
        }

        if (subject.Contains("presence_penalty") || subject.Contains("frequency_penalty"))
        {
            if (resolved.PresencePenalty is not { } current)
            {
                return null;
            }

            var clamped = Clamp(current, min, max);
            return Math.Abs(clamped - current) < 1e-9 ? null : resolved with { PresencePenalty = clamped };
        }

        if (subject.Contains("max_tokens"))
        {
            var clamped = (int)Math.Round(Clamp(resolved.MaxTokens, min, max));
            return clamped == resolved.MaxTokens ? null : resolved with { MaxTokens = clamped };
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Extracts <c>[min, max]</c> from a provider message; null when no usable range is stated.</summary>
    private static (double Min, double Max)? ExtractRange(string message)
    {
        var open = message.IndexOf('[');
        var comma = open >= 0 ? message.IndexOf(',', open + 1) : -1;
        var close = comma >= 0 ? message.IndexOf(']', comma + 1) : -1;
        if (open < 0 || comma < 0 || close < 0)
        {
            return null;
        }

        var minText = message[(open + 1)..comma].Trim();
        var maxText = message[(comma + 1)..close].Trim();

        return double.TryParse(minText, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
            && double.TryParse(maxText, NumberStyles.Float, CultureInfo.InvariantCulture, out var max)
            && min <= max
                ? (min, max)
                : null;
    }

    /// <summary>Extracts <c>error.message</c> from a provider error body, or null when there is none.</summary>
    private static string? TryReadProviderMessage(string errorContent)
    {
        if (string.IsNullOrWhiteSpace(errorContent))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(errorContent);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var message = ReadString(error, "message");
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var flattened = message.ReplaceLineEndings(" ").Trim();
            return flattened.Length > 300 ? flattened[..300] + "…" : flattened;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

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

        // B-167: sent only when the function's row declares one; omitted otherwise so a provider that rejects the
        // parameter sees exactly the request it saw before this existed.
        [JsonPropertyName("presence_penalty")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? PresencePenalty { get; init; }

        // DeepSeek's thinking toggle; omitted for every other provider.
        [JsonPropertyName("thinking")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Thinking { get; init; }

        // Reproducible sampling for A/B prose comparisons; omitted so the provider chooses by default.
        [JsonPropertyName("seed")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? Seed { get; init; }

        [JsonPropertyName("chat_template_kwargs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object>? ChatTemplateKwargs { get; init; }
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
