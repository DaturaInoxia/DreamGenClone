using System.Net.Http.Headers;
using System.Security.Cryptography;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

public sealed class ImageEditorEndpointReadiness : IImageEditorEndpointReadiness
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<ImageEditorEndpointReadiness> _logger;

    public ImageEditorEndpointReadiness(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<ImageEditorEndpointReadiness> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<bool> IsWarmAsync(
        ResolvedImageEditorModel model,
        CancellationToken cancellationToken = default)
    {
        if (model.ImageProtocol != ImageProtocol.ComfyUiServerless
            || string.IsNullOrWhiteSpace(model.ComfyUiUrl)
            || string.IsNullOrWhiteSpace(model.ApiKeyEncrypted))
        {
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            client.Timeout = TimeSpan.FromSeconds(model.ProviderTimeoutSeconds);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", _encryptionService.Decrypt(model.ApiKeyEncrypted));
            using var response = await client.GetAsync(
                $"{model.ComfyUiUrl.TrimEnd('/')}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or CryptographicException)
        {
            _logger.LogDebug(exception, "Image editor endpoint is not warm: Provider={ProviderName}", model.ProviderName);
            return false;
        }
    }
}