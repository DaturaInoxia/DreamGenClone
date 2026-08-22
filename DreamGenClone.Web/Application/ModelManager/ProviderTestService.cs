using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.ModelManager;

public sealed class ProviderTestService
{
    private readonly ICompletionClient _completionClient;
    private readonly IImageGenerationClient _imageGenerationClient;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly IProviderRepository _providerRepository;
    private readonly ILogger<ProviderTestService> _logger;

    public ProviderTestService(
        ICompletionClient completionClient,
        IImageGenerationClient imageGenerationClient,
        IApiKeyEncryptionService encryptionService,
        IProviderRepository providerRepository,
        ILogger<ProviderTestService> logger)
    {
        _completionClient = completionClient;
        _imageGenerationClient = imageGenerationClient;
        _encryptionService = encryptionService;
        _providerRepository = providerRepository;
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

    public async Task<(bool Success, string Message)> TestModelConnectionAsync(RegisteredModel model, CancellationToken cancellationToken = default)
    {
        var provider = await _providerRepository.GetByIdAsync(model.ProviderId, cancellationToken);
        if (provider is null)
            return (false, "Parent provider not found.");

        if (!provider.IsEnabled)
            return (false, "Parent provider is disabled.");

        string? decryptedKey = null;
        if (!string.IsNullOrEmpty(provider.ApiKeyEncrypted))
        {
            try
            {
                decryptedKey = _encryptionService.Decrypt(provider.ApiKeyEncrypted);
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                _logger.LogError(ex, "Failed to decrypt API key for provider {ProviderName} while testing model {ModelName}.", provider.Name, model.DisplayName);
                return (false, "API key decryption failed. Please re-enter the API key on the provider.");
            }
        }

        // Image-kind models are served at the image-generation endpoint (not chat completions),
        // so probe the image path for them. Text models keep the existing chat health check.
        if (model.ModelKind == ModelKind.Image)
        {
            _logger.LogInformation(
                "Testing image model connection: Model={ModelIdentifier}, Provider={ProviderName}, Path={ImagePath}",
                model.ModelIdentifier,
                provider.Name,
                provider.ImageGenerationPath);

            return await _imageGenerationClient.CheckImageModelHealthAsync(
                provider.BaseUrl,
                provider.ImageGenerationPath,
                provider.TimeoutSeconds,
                decryptedKey,
                model.ModelIdentifier,
                provider.ContentPolicy,
                cancellationToken);
        }

        return await _completionClient.CheckModelHealthAsync(
            provider.BaseUrl,
            provider.ChatCompletionsPath,
            provider.TimeoutSeconds,
            decryptedKey,
            model.ModelIdentifier,
            cancellationToken);
    }
}
