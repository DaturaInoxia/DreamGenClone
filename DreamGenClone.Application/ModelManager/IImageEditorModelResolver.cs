using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.ModelManager;

/// <summary>Resolves the configured source-image editor without sharing the text-to-image path.</summary>
public interface IImageEditorModelResolver
{
    Task<ResolvedImageEditorModel> ResolveAsync(CancellationToken cancellationToken = default);
}