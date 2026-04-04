using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.ModelManager;

public interface IFunctionDefaultRepository
{
    Task<FunctionModelDefault> SaveAsync(FunctionModelDefault functionDefault, CancellationToken cancellationToken = default);
    Task<FunctionModelDefault?> GetByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default);
    Task<List<FunctionModelDefault>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<FunctionModelDefault>> GetByModelIdAsync(string modelId, CancellationToken cancellationToken = default);
    Task<bool> DeleteByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default);
}
