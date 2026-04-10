using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.ModelManager;

public interface IHealthCheckRepository
{
    Task SaveBatchAsync(List<HealthCheckResult> results, CancellationToken cancellationToken = default);
    Task<List<HealthCheckResult>> GetAllAsync(CancellationToken cancellationToken = default);
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
