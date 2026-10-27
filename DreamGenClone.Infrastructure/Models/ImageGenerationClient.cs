using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// OpenAI-compatible image generation client for the <c>/v1/images/generations</c> endpoint.
/// Mirrors <see cref="CompletionClient"/>: same named HTTP client, DPAPI key decryption, bearer
/// auth for cloud providers, and HTTP error mapping.
/// </summary>
public sealed class ImageGenerationClient : IImageGenerationClient
{
    // Together's Cloudflare rejects non-browser User-Agents with 403 error 1010 (verified
    // 2026-08-30 on the gpt-image-2 base). Set on the per-request HttpClient instance returned by
    // CreateClient (never on a shared pool), so it only affects image-generation requests.
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/120.0 Safari/537.36";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<ImageGenerationClient> _logger;

    public ImageGenerationClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<ImageGenerationClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<byte[]?> GenerateAsync(
        ResolvedImageModel model,
        string prompt,
        string? size,
        string? negativePrompt = null,
        long? seed = null,
        CancellationToken cancellationToken = default,
        SceneImageGenerationOptions? options = null)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");

            // Ensure BaseAddress ends with "/" so relative path resolution works correctly.
            var baseUrl = model.ProviderBaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(model.ProviderTimeoutSeconds);

            if (!string.IsNullOrEmpty(model.ApiKeyEncrypted))
            {
                try
                {
                    var decryptedKey = _encryptionService.Decrypt(model.ApiKeyEncrypted);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedKey);
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    _logger.LogError(ex,
                        "Failed to decrypt API key for provider {ProviderName}. Please re-enter the API key in Model Manager.",
                        model.ProviderName);
                    throw;
                }
            }

            // Browser UA for the OpenAI-compatible images endpoint (Cloudflare 403 error 1010
            // otherwise, verified on gpt-image-2/Together).
            if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
            }

            var payload = new ImageGenerationRequest
            {
                Model = model.ModelIdentifier,
                Prompt = prompt,
                N = 1,
                Width = TryParseDimension(size, out var w) ? w : 1024,
                Height = TryParseDimension(size, out var h) ? h : 1024,
                ResponseFormat = "base64"
            };

            // Strip leading "/" so the path resolves relative to BaseAddress, not root.
            var relativePath = model.ImageGenerationPath.TrimStart('/');

            _logger.LogInformation(
                "Image generation request start: Model={ModelIdentifier}, Provider={ProviderName}, SessionOverride={IsSessionOverride}, Policy={ContentPolicy}, TimeoutSeconds={TimeoutSeconds}, PromptChars={PromptChars}",
                model.ModelIdentifier,
                model.ProviderName,
                model.IsSessionOverride,
                model.ContentPolicy,
                model.ProviderTimeoutSeconds,
                prompt.Length);

            using var response = await client.PostAsJsonAsync(relativePath, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var statusCode = (int)response.StatusCode;

                var (errorMessage, reasonCode) = statusCode switch
                {
                    401 => ($"Invalid API key for provider {model.ProviderName}", "invalid_api_key"),
                    429 => ($"Rate limit exceeded for provider {model.ProviderName}", "rate_limit"),
                    402 => ($"Payment required for provider {model.ProviderName}", "payment_required"),
                    >= 500 => ($"Server error from provider {model.ProviderName}: {statusCode}", "server_error"),
                    _ => ($"Request failed for provider {model.ProviderName}: {statusCode}", "request_failed")
                };

                _logger.LogWarning(
                    "Image generation request failed: {ErrorMessage}, StatusCode={StatusCode}, Response={ErrorContent}",
                    errorMessage,
                    statusCode,
                    errorContent);

                throw new ImageGenerationException(errorMessage, model.ProviderName, statusCode, reasonCode);
            }

            var responseBody = await response.Content.ReadFromJsonAsync<ImageGenerationResponse>(cancellationToken);
            var b64 = responseBody?.Data?.FirstOrDefault()?.B64Json;

            stopwatch.Stop();
            if (string.IsNullOrEmpty(b64))
            {
                _logger.LogWarning(
                    "Image generation response contained no image data: Model={ModelIdentifier}, Provider={ProviderName}, DurationMs={DurationMs}",
                    model.ModelIdentifier,
                    model.ProviderName,
                    stopwatch.ElapsedMilliseconds);
                return null;
            }

            var imageBytes = Convert.FromBase64String(b64);
            _logger.LogInformation(
                "Image generation completed: Model={ModelIdentifier}, Provider={ProviderName}, Bytes={Bytes}, DurationMs={DurationMs}",
                model.ModelIdentifier,
                model.ProviderName,
                imageBytes.Length,
                stopwatch.ElapsedMilliseconds);

            return imageBytes;
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Image generation call failed for provider {ProviderName} after {DurationMs}ms",
                model.ProviderName,
                stopwatch.ElapsedMilliseconds);
            throw new ImageGenerationException($"Image generation failed: {ex.Message}", model.ProviderName, reasonCode: "client_error", inner: ex);
        }
    }

    public async Task<(bool Success, string Message)> CheckImageModelHealthAsync(
        string providerBaseUrl,
        string imageGenerationPath,
        int timeoutSeconds,
        string? decryptedApiKey,
        string modelIdentifier,
        ImageContentPolicy contentPolicy,
        CancellationToken cancellationToken = default,
        ImageProtocol imageProtocol = ImageProtocol.OpenAiImages)
    {
        // contentPolicy is deliberately unused: discovering negative_prompt / disable_safety_checker support
        // required a real 1024x1024 render per parameter, so that probing is gone for good.
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

            // Browser UA for Together's Cloudflare edge (403 error 1010 otherwise, verified on gpt-image-2).
            if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
            }

            // Health checks must never spend money. The image endpoint has no free readiness route, so the
            // check is a GET on the provider's model list: it costs nothing and its 401/403 still proves the
            // key. Nothing is rendered here by design — a bad key or an empty balance surfaces when the user
            // actually generates.
            var probe = await ProviderModelsProbe.QueryAsync(client, imageGenerationPath, modelIdentifier, cancellationToken);

            switch (probe.StatusCode)
            {
                case 401 or 403:
                    _logger.LogWarning("Image model health check failed: key rejected for {ModelIdentifier} at {BaseUrl} (HTTP {Status})", modelIdentifier, providerBaseUrl, probe.StatusCode);
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
                _logger.LogInformation("Image model health check: {ModelIdentifier} at {BaseUrl} exposes no model list (HTTP {Status}); reachability only, nothing was generated", modelIdentifier, providerBaseUrl, probe.StatusCode);
                return (true, $"Provider is reachable, but it exposes no model list (HTTP {probe.StatusCode}) to check the key against. "
                    + "No image was generated, so the key and credits stay unverified until the first real generation.");
            }

            if (probe.StatusCode >= 400)
            {
                return (false, $"Model list request failed (HTTP {probe.StatusCode}).");
            }

            if (!probe.ContainsModel)
            {
                _logger.LogWarning("Image model health check: {ModelIdentifier} is not listed at {BaseUrl}", modelIdentifier, providerBaseUrl);
                return (false, $"API key accepted, but '{modelIdentifier}' is not in the provider's model list. "
                    + "No image was generated to verify it — generation will fail if the model really is unavailable.");
            }

            _logger.LogInformation("Image model health check passed (model list, nothing generated): {ModelIdentifier} at {BaseUrl}", modelIdentifier, providerBaseUrl);
            return (true, $"API key accepted and '{modelIdentifier}' is listed by the provider. No image was generated.");
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
            _logger.LogWarning(ex, "Image model health check failed: {ModelIdentifier} at {BaseUrl}", modelIdentifier, providerBaseUrl);
            return (false, $"Error: {ex.Message}");
        }
    }

    /// <summary>Parses an OpenAI-style "WxH" size tag into integer width/height. Returns false when
    /// the tag is missing/malformed so the caller can fall back to the recorded defaults.</summary>
    private static bool TryParseDimension(string? size, out int dimension)
    {
        dimension = 0;
        if (string.IsNullOrWhiteSpace(size)) return false;
        var parts = size.Trim().ToLowerInvariant().Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out dimension) && dimension > 0;
    }

    /// <summary>Request body for a real generation. Never constructed by a health check.</summary>
    private sealed class ImageGenerationRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = string.Empty;
        [JsonPropertyName("n")] public int N { get; set; } = 1;
        [JsonPropertyName("width")] public int Width { get; set; } = 1024;
        [JsonPropertyName("height")] public int Height { get; set; } = 1024;
        [JsonPropertyName("response_format")] public string ResponseFormat { get; set; } = "base64";
    }

    private sealed class ImageGenerationResponse
    {
        [JsonPropertyName("data")] public List<ImageGenerationData>? Data { get; set; }
    }

    private sealed class ImageGenerationData
    {
        [JsonPropertyName("b64_json")] public string? B64Json { get; set; }
    }
}