namespace DreamGenClone.Web.Application.Assistants;

public interface IWritingAssistantService
{
    Task<string> GenerateSuggestionAsync(string? scenarioSummary, IReadOnlyList<string> recentStoryBlocks, string userPrompt, CancellationToken cancellationToken = default);
}
