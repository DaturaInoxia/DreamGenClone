using DreamGenClone.Application.StoryParser.Models;

namespace DreamGenClone.Application.StoryParser;

public interface IStoryParserService
{
    Task<StoryParseResult> ParseFromUrlAsync(StoryParseRequest request, CancellationToken cancellationToken = default);

    Task<ParsedStoryDetail?> GetParsedStoryAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> DeleteParsedStoryAsync(string id, CancellationToken cancellationToken = default);
}
