using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.ModelManager;

public interface IRegisteredModelRepository
{
    Task<RegisteredModel> SaveAsync(RegisteredModel model, CancellationToken cancellationToken = default);
    Task<RegisteredModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<List<RegisteredModel>> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default);
    Task<List<RegisteredModel>> GetAllEnabledAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByProviderAndIdentifierAsync(string providerId, string modelIdentifier, CancellationToken cancellationToken = default);
}
