using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.ModelManager;

public sealed class ProviderTestService
{
    private readonly ICompletionClient _completionClient;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<ProviderTestService> _logger;

    public ProviderTestService(
        ICompletionClient completionClient,
        IApiKeyEncryptionService encryptionService,
        ILogger<ProviderTestService> logger)
    {
        _completionClient = completionClient;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(Provider provider, CancellationToken cancellationToken = default)
    {
        try
        {
            string? decryptedKey = null;
            if (!string.IsNullOrEmpty(provider.ApiKeyEncrypted))
            {
                try
                {
                    decryptedKey = _encryptionService.Decrypt(provider.ApiKeyEncrypted);
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    _logger.LogError(ex, "Failed to decrypt API key for provider {ProviderName}. Please re-enter the API key in Model Manager.", provider.Name);
                    return (false, "API key decryption failed. Please re-enter the API key.");
                }
            }

            var isHealthy = await _completionClient.CheckHealthAsync(
                provider.BaseUrl,
                provider.TimeoutSeconds,
                decryptedKey,
                cancellationToken);

            if (isHealthy)
            {
                _logger.LogInformation("Connection test passed for provider {ProviderName}", provider.Name);
                return (true, "Connection successful!");
            }

            return (false, "Connection failed. Check the base URL and ensure the provider is running.");
        }
        catch (TaskCanceledException)
        {
            return (false, "Connection timed out. Check the base URL or increase the timeout.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Connection error: {ex.Message}");
        }
    }
}
