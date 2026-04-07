using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.ModelManager;

public sealed class HealthCheckService : IHealthCheckService
{
    private readonly IProviderRepository _providerRepository;
    private readonly IRegisteredModelRepository _modelRepository;
    private readonly ProviderTestService _testService;
    private readonly IHealthCheckRepository _healthCheckRepository;
    private readonly ILogger<HealthCheckService> _logger;

    public HealthCheckService(
        IProviderRepository providerRepository,
        IRegisteredModelRepository modelRepository,
        ProviderTestService testService,
        IHealthCheckRepository healthCheckRepository,
        ILogger<HealthCheckService> logger)
    {
        _providerRepository = providerRepository;
        _modelRepository = modelRepository;
        _testService = testService;
        _healthCheckRepository = healthCheckRepository;
        _logger = logger;
    }

    public async Task<List<HealthCheckResult>> RunAllHealthChecksAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting health checks for all providers and models");
        var results = new List<HealthCheckResult>();
        var providers = await _providerRepository.GetAllAsync(cancellationToken);

        var semaphore = new SemaphoreSlim(3);
        var tasks = providers.Select(async provider =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await CheckProviderAndModelsAsync(provider, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var allResults = await Task.WhenAll(tasks);
        foreach (var batch in allResults)
        {
            results.AddRange(batch);
        }

        await _healthCheckRepository.SaveBatchAsync(results, cancellationToken);
        _logger.LogInformation("Health checks completed: {Count} results", results.Count);
        return results;
    }

    private async Task<List<HealthCheckResult>> CheckProviderAndModelsAsync(Provider provider, CancellationToken cancellationToken)
    {
        var results = new List<HealthCheckResult>();

        // Test provider connectivity
        var (providerSuccess, providerMessage) = await _testService.TestConnectionAsync(provider, cancellationToken);

        results.Add(new HealthCheckResult
        {
            EntityType = HealthCheckEntityType.Provider,
            EntityId = provider.Id,
            EntityName = provider.Name,
            ProviderName = provider.Name,
            IsHealthy = providerSuccess,
            Message = providerMessage,
            CheckedUtc = DateTime.UtcNow.ToString("o")
        });

        // Test each model under this provider
        var models = await _modelRepository.GetByProviderIdAsync(provider.Id, cancellationToken);
        foreach (var model in models)
        {
            if (!providerSuccess)
            {
                // Skip model test if provider is unreachable
                results.Add(new HealthCheckResult
                {
                    EntityType = HealthCheckEntityType.Model,
                    EntityId = model.Id,
                    EntityName = model.DisplayName,
                    ProviderName = provider.Name,
                    IsHealthy = false,
                    Message = "Skipped — provider is unreachable.",
                    CheckedUtc = DateTime.UtcNow.ToString("o")
                });
                continue;
            }

            var (modelSuccess, modelMessage) = await _testService.TestModelConnectionAsync(model, cancellationToken);
            results.Add(new HealthCheckResult
            {
                EntityType = HealthCheckEntityType.Model,
                EntityId = model.Id,
                EntityName = model.DisplayName,
                ProviderName = provider.Name,
                IsHealthy = modelSuccess,
                Message = modelMessage,
                CheckedUtc = DateTime.UtcNow.ToString("o")
            });
        }

        return results;
    }
}
