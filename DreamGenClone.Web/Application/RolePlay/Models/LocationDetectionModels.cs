namespace DreamGenClone.Web.Application.RolePlay.Models;

public sealed class LocationDetectionRequest
{
    public required string SessionId { get; init; }
    public required IReadOnlyList<string> RecentInteractions { get; init; }
    public required IReadOnlyList<string> ScenarioLocationNames { get; init; }
    public string? PreviousLocation { get; init; }
    public required IReadOnlyList<string> CharacterNames { get; init; }
}

public sealed record LocationDetectionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? DetectedLocation { get; init; }
    public decimal? LocationConfidence { get; init; }
    public IReadOnlyDictionary<string, string?>? PerCharacterLocations { get; init; }
    public bool LocationChanged { get; init; }
    public string? Reasoning { get; init; }
    public required string RawModelOutput { get; init; }
    public required string PromptSystem { get; init; }
    public required string PromptUser { get; init; }
}
