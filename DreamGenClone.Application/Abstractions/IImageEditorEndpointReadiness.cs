using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

public interface IImageEditorEndpointReadiness
{
    Task<bool> IsWarmAsync(
        ResolvedImageEditorModel model,
        CancellationToken cancellationToken = default);
}