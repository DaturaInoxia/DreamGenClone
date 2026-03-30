using DreamGenClone.Application.StoryParser;
using DreamGenClone.Application.StoryParser.Models;
using DreamGenClone.Domain.StoryParser;

namespace DreamGenClone.Web.Application.StoryParser;

public sealed class StoryCatalogFacade
{
    private readonly IStoryCatalogService _service;

    public StoryCatalogFacade(IStoryCatalogService service)
    {
        _service = service;
    }

    public Task<IReadOnlyList<StoryCatalogEntry>> ListAsync(CatalogSortMode sortMode, CancellationToken cancellationToken = default)
    {
        return _service.ListAsync(new StoryCatalogQuery { SortMode = sortMode }, cancellationToken);
    }

    public Task<IReadOnlyList<StoryCatalogEntry>> ListAsync(CatalogSortMode sortMode, bool includeArchived, CancellationToken cancellationToken = default)
    {
        return _service.ListAsync(new StoryCatalogQuery { SortMode = sortMode }, includeArchived, cancellationToken);
    }

    public Task<IReadOnlyList<StoryCatalogEntry>> SearchAsync(string query, CatalogSortMode sortMode, CancellationToken cancellationToken = default)
    {
        return _service.SearchAsync(new StoryCatalogSearch { Query = query, SortMode = sortMode }, cancellationToken);
    }
}
