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
        CancellationToken cancellationToken = default)
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

            // Minimal image request to the image-generation path. Seedream/FLUX-style image models
            // do not accept chat-completions bodies, so we probe the images endpoint instead.
            var payload = new ImageGenerationRequest
            {
                Model = modelIdentifier,
                Prompt = "a colored square",
                N = 1,
                Width = 1024,
                Height = 1024,
                ResponseFormat = "base64"
            };

            var relativePath = imageGenerationPath.TrimStart('/');
            using var response = await client.PostAsJsonAsync(relativePath, payload, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Image model health check passed: {ModelIdentifier} at {BaseUrl}", modelIdentifier, providerBaseUrl);
                return (true, "Image model is reachable and responding.");
            }

            var statusCode = (int)response.StatusCode;
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var errorMessage = statusCode switch
            {
                401 => "Invalid API key.",
                404 => $"Model '{modelIdentifier}' not found on provider.",
                429 => "Rate limit exceeded.",
                402 => "Payment required.",
                >= 500 => $"Provider server error ({statusCode}).",
                _ => $"Unexpected status {statusCode}."
            };

            _logger.LogWarning(
                "Image model health check failed: {ModelIdentifier} at {BaseUrl}, Status={StatusCode}, Response={ErrorContent}",
                modelIdentifier,
                providerBaseUrl,
                statusCode,
                errorContent);
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
            _logger.LogWarning(ex, "Image model health check failed: {ModelIdentifier} at {BaseUrl}", modelIdentifier, providerBaseUrl);
            return (false, $"Error: {ex.Message}");
        }
    }

    private sealed class ImageGenerationRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = string.Empty;
        [JsonPropertyName("n")] public int N { get; set; } = 1;
        [JsonPropertyName("width")] public int Width { get; set; } = 1024;
        [JsonPropertyName("height")] public int Height { get; set; } = 1024;
        [JsonPropertyName("response_format")] public string ResponseFormat { get; set; } = "base64";
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

    private sealed class ImageGenerationResponse
    {
        [JsonPropertyName("data")] public List<ImageGenerationData>? Data { get; set; }
    }

    private sealed class ImageGenerationData
    {
        [JsonPropertyName("b64_json")] public string? B64Json { get; set; }
    }
}