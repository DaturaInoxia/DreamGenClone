using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.ModelManager;

public interface IMultimodalModelResolutionService
{
    Task<ResolvedMultimodalModel> ResolveAsync(
        AppFunction function,
        CancellationToken cancellationToken = default);
}