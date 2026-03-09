namespace DreamGenClone.Infrastructure.Storage;

public interface ITemplateImageStorageService
{
    Task<string> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);
}
