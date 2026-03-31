namespace DreamGenClone.Application.StoryParser;

public interface ICollectionMatchingService
{
    Task<Models.CollectionMatchResult> FindMatchesAsync(string sourceUrl, string? title, CancellationToken cancellationToken = default);
}
