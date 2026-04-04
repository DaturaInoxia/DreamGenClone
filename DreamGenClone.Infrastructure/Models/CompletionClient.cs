using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    private async Task<string> SendCompletionAsync(
        List<ChatMessage> messages,
        ResolvedModel resolved,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        var client = _httpClientFactory.CreateClient("CompletionClient");
        client.BaseAddress = new Uri(resolved.ProviderBaseUrl);
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

        using var response = await client.PostAsJsonAsync(resolved.ChatCompletionsPath, payload, cancellationToken);

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

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken);
        var content = result?.Choices?.FirstOrDefault()?.Message?.Content;

        var duration = DateTime.UtcNow - startTime;
        _logger.LogInformation(
            "Completion request completed: Model={ModelIdentifier}, Provider={ProviderName}, SessionOverride={IsSessionOverride}, Duration={DurationMs}ms",
            resolved.ModelIdentifier, resolved.ProviderName, resolved.IsSessionOverride, (int)duration.TotalMilliseconds);

        return content ?? string.Empty;
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
    }

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; init; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; init; }
    }
}
