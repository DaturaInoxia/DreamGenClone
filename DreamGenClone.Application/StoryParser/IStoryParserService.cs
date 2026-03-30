using DreamGenClone.Application.StoryParser.Models;
using DreamGenClone.Domain.StoryParser;

namespace DreamGenClone.Application.StoryParser;

public interface IStoryParserService
{
    Task<StoryParseResult> ParseFromUrlAsync(StoryParseRequest request, CancellationToken cancellationToken = default);

    Task<ParsedStoryDetail?> GetParsedStoryAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> DeleteParsedStoryAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> ArchiveParsedStoryAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> UnarchiveParsedStoryAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> PurgeParsedStoryAsync(string id, CancellationToken cancellationToken = default);

    Task<List<ParsedStoryRecord>> FindBySourceUrlAsync(string sourceUrl, CancellationToken cancellationToken = default);
}
