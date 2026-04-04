using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.ModelManager;

public sealed class ModelManagerFacade
{
    private readonly IProviderRepository _providerRepository;
    private readonly IRegisteredModelRepository _modelRepository;
    private readonly IFunctionDefaultRepository _functionDefaultRepository;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ProviderTestService _testService;
    private readonly ILogger<ModelManagerFacade> _logger;

    public ModelManagerFacade(
        IProviderRepository providerRepository,
        IRegisteredModelRepository modelRepository,
        IFunctionDefaultRepository functionDefaultRepository,
        IApiKeyEncryptionService encryptionService,
        ProviderTestService testService,
        ILogger<ModelManagerFacade> logger)
    {
        _providerRepository = providerRepository;
        _modelRepository = modelRepository;
        _functionDefaultRepository = functionDefaultRepository;
        _encryptionService = encryptionService;
        _testService = testService;
        _logger = logger;
    }

    // === Provider Methods (US1) ===

    public async Task<List<Provider>> GetAllProvidersAsync(CancellationToken cancellationToken = default)
    {
        return await _providerRepository.GetAllAsync(cancellationToken);
    }

    public async Task<Provider> SaveProviderAsync(Provider provider, string? plainTextApiKey, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(plainTextApiKey))
        {
            provider.ApiKeyEncrypted = _encryptionService.Encrypt(plainTextApiKey);
        }

        var result = await _providerRepository.SaveAsync(provider, cancellationToken);
        _logger.LogInformation("Provider saved: {ProviderId} ({ProviderName}), Type={ProviderType}", result.Id, result.Name, result.ProviderType);
        return result;
    }

    public async Task<bool> DeleteProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var result = await _providerRepository.DeleteAsync(providerId, cancellationToken);
        _logger.LogInformation("Provider deleted: {ProviderId}", providerId);
        return result;
    }

    public async Task<(bool Success, string Message)> TestProviderConnectionAsync(Provider provider, CancellationToken cancellationToken = default)
    {
        return await _testService.TestConnectionAsync(provider, cancellationToken);
    }

    public async Task<Provider> EnableDisableProviderAsync(string providerId, bool enabled, CancellationToken cancellationToken = default)
    {
        var provider = await _providerRepository.GetByIdAsync(providerId, cancellationToken);
        if (provider is null) throw new InvalidOperationException($"Provider '{providerId}' not found.");

        provider.IsEnabled = enabled;
        await _providerRepository.SaveAsync(provider, cancellationToken);
        _logger.LogInformation("Provider {Action}: {ProviderId} ({ProviderName})", enabled ? "enabled" : "disabled", providerId, provider.Name);
        return provider;
    }

    public async Task<List<FunctionModelDefault>> GetDependentFunctionDefaultsAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return await _functionDefaultRepository.GetByModelIdAsync(modelId, cancellationToken);
    }

    // === Model Methods (US2) ===

    public async Task<List<RegisteredModel>> GetModelsByProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        return await _modelRepository.GetByProviderIdAsync(providerId, cancellationToken);
    }

    public async Task<Dictionary<Provider, List<RegisteredModel>>> GetAllModelsGroupedByProviderAsync(CancellationToken cancellationToken = default)
    {
        var providers = await _providerRepository.GetAllAsync(cancellationToken);
        var result = new Dictionary<Provider, List<RegisteredModel>>();

        foreach (var provider in providers)
        {
            var models = await _modelRepository.GetByProviderIdAsync(provider.Id, cancellationToken);
            result[provider] = models;
        }

        return result;
    }

    public async Task<RegisteredModel> SaveModelAsync(RegisteredModel model, CancellationToken cancellationToken = default)
    {
        var result = await _modelRepository.SaveAsync(model, cancellationToken);
        _logger.LogInformation("Model saved: {ModelId} ({DisplayName})", result.Id, result.DisplayName);
        return result;
    }

    public async Task<bool> DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        // Clear function defaults pointing to this model first
        var dependentDefaults = await _functionDefaultRepository.GetByModelIdAsync(modelId, cancellationToken);
        foreach (var fd in dependentDefaults)
        {
            if (Enum.TryParse<AppFunction>(fd.FunctionName, out var func))
            {
                await _functionDefaultRepository.DeleteByFunctionAsync(func, cancellationToken);
            }
        }

        var result = await _modelRepository.DeleteAsync(modelId, cancellationToken);
        _logger.LogInformation("Model deleted: {ModelId}", modelId);
        return result;
    }

    public async Task<RegisteredModel> EnableDisableModelAsync(string modelId, bool enabled, CancellationToken cancellationToken = default)
    {
        var model = await _modelRepository.GetByIdAsync(modelId, cancellationToken);
        if (model is null) throw new InvalidOperationException($"Model '{modelId}' not found.");

        model.IsEnabled = enabled;
        await _modelRepository.SaveAsync(model, cancellationToken);
        _logger.LogInformation("Model {Action}: {ModelId} ({DisplayName})", enabled ? "enabled" : "disabled", modelId, model.DisplayName);
        return model;
    }

    // === Function Default Methods (US3) ===

    public async Task<List<(AppFunction Function, FunctionModelDefault? Default)>> GetAllFunctionDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var defaults = await _functionDefaultRepository.GetAllAsync(cancellationToken);
        var defaultMap = defaults.ToDictionary(d => d.FunctionName, d => d);

        return Enum.GetValues<AppFunction>()
            .Select(f => (f, defaultMap.GetValueOrDefault(f.ToString())))
            .ToList();
    }

    public async Task<FunctionModelDefault> SaveFunctionDefaultAsync(FunctionModelDefault functionDefault, CancellationToken cancellationToken = default)
    {
        var result = await _functionDefaultRepository.SaveAsync(functionDefault, cancellationToken);
        _logger.LogInformation("Function default saved: {FunctionName} → Model={ModelId}", result.FunctionName, result.ModelId);
        return result;
    }

    public async Task<bool> ClearFunctionDefaultAsync(AppFunction function, CancellationToken cancellationToken = default)
    {
        var result = await _functionDefaultRepository.DeleteByFunctionAsync(function, cancellationToken);
        _logger.LogInformation("Function default cleared: {Function}", function);
        return result;
    }

    public async Task<List<(Provider Provider, List<RegisteredModel> Models)>> GetEnabledModelsForDropdownAsync(CancellationToken cancellationToken = default)
    {
        var providers = await _providerRepository.GetAllAsync(cancellationToken);
        var result = new List<(Provider, List<RegisteredModel>)>();

        foreach (var provider in providers.Where(p => p.IsEnabled))
        {
            var models = await _modelRepository.GetByProviderIdAsync(provider.Id, cancellationToken);
            var enabledModels = models.Where(m => m.IsEnabled).ToList();
            if (enabledModels.Count > 0)
            {
                result.Add((provider, enabledModels));
            }
        }

        return result;
    }
}
