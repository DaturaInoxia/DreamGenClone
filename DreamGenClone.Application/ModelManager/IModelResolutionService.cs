using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.ModelManager;

public interface IModelResolutionService
{
    Task<ResolvedModel> ResolveAsync(
        AppFunction function,
        string? sessionModelId = null,
        double? sessionTemperature = null,
        double? sessionTopP = null,
        int? sessionMaxTokens = null,
        CancellationToken cancellationToken = default);
}
