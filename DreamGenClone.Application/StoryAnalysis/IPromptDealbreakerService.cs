namespace DreamGenClone.Application.StoryAnalysis;

public interface IPromptDealbreakerService
{
    Task<PromptDealbreakerResult> ValidateAsync(string text, string profileId, CancellationToken cancellationToken = default);
}

public sealed class PromptDealbreakerResult
{
    public bool IsAllowed { get; set; } = true;

    public List<string> ViolatedThemes { get; set; } = [];

    public string? Message { get; set; }
}