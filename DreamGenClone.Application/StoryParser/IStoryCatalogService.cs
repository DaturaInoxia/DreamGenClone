using DreamGenClone.Application.StoryParser.Models;

namespace DreamGenClone.Application.StoryParser;

public interface IStoryCatalogService
{
    Task<IReadOnlyList<StoryCatalogEntry>> ListAsync(StoryCatalogQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoryCatalogEntry>> SearchAsync(StoryCatalogSearch query, CancellationToken cancellationToken = default);
}
