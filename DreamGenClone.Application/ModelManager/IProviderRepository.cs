using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.ModelManager;

public interface IProviderRepository
{
    Task<Provider> SaveAsync(Provider provider, CancellationToken cancellationToken = default);
    Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<List<Provider>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
}
