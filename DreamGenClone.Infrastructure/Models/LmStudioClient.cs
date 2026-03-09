using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.Models;

public sealed class LmStudioClient : ILmStudioClient
{
    private readonly HttpClient _httpClient;
    private readonly LmStudioOptions _options;
    private readonly ILogger<LmStudioClient> _logger;

    public LmStudioClient(HttpClient httpClient, IOptions<LmStudioOptions> options, ILogger<LmStudioClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/", cancellationToken);
            var isHealthy = response.IsSuccessStatusCode;
            _logger.LogInformation("LM Studio health check completed with status {StatusCode}", (int)response.StatusCode);
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LM Studio health check failed");
            return false;
        }
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var payload = new ChatRequest
        {
            Messages =
            [
                new ChatMessage("user", prompt)
            ]
        };

        using var response = await _httpClient.PostAsJsonAsync(_options.ChatCompletionsPath, payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken);
        var content = result?.Choices?.FirstOrDefault()?.Message?.Content;

        _logger.LogInformation("LM Studio generation request completed");

        return content ?? string.Empty;
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; init; } = [];
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
