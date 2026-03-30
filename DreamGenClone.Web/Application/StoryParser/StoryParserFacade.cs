using DreamGenClone.Application.StoryParser;
using DreamGenClone.Application.StoryParser.Models;
using DreamGenClone.Domain.StoryParser;

namespace DreamGenClone.Web.Application.StoryParser;

public sealed class StoryParserFacade
{
    private readonly IStoryParserService _service;

    public StoryParserFacade(IStoryParserService service)
    {
        _service = service;
    }

    public Task<StoryParseResult> ParseFromUrlAsync(StoryParseRequest request, CancellationToken cancellationToken = default)
    {
        return _service.ParseFromUrlAsync(request, cancellationToken);
    }

    public Task<ParsedStoryDetail?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return _service.GetParsedStoryAsync(id, cancellationToken);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return _service.DeleteParsedStoryAsync(id, cancellationToken);
    }

    public Task<bool> ArchiveAsync(string id, CancellationToken cancellationToken = default)
    {
        return _service.ArchiveParsedStoryAsync(id, cancellationToken);
    }

    public Task<bool> UnarchiveAsync(string id, CancellationToken cancellationToken = default)
    {
        return _service.UnarchiveParsedStoryAsync(id, cancellationToken);
    }

    public Task<bool> PurgeAsync(string id, CancellationToken cancellationToken = default)
    {
        return _service.PurgeParsedStoryAsync(id, cancellationToken);
    }

    public Task<List<ParsedStoryRecord>> FindBySourceUrlAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        return _service.FindBySourceUrlAsync(sourceUrl, cancellationToken);
    }
}
