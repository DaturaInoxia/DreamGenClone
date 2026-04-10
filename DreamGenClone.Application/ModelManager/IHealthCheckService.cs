using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.ModelManager;

public interface IHealthCheckService
{
    Task<List<HealthCheckResult>> RunAllHealthChecksAsync(CancellationToken cancellationToken = default);
}
