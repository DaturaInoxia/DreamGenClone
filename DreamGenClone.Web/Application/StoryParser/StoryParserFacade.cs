using DreamGenClone.Application.StoryParser;
using DreamGenClone.Application.StoryParser.Models;

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
}
