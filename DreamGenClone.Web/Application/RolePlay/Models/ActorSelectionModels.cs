namespace DreamGenClone.Web.Application.RolePlay.Models;

public enum ActorSelectionSource
{
    LLM,
    Cache,
    Scoring,
    Fallback
}

public sealed class ActorSelectionRequest
{
    public required string SessionId { get; init; }
    public required string NarrativeSummary { get; init; }
    public required string CurrentPhase { get; init; }
    public string? CurrentLocation { get; init; }
    public string? CurrentTimeOfDay { get; init; }
    public required IReadOnlyList<ActorCandidateInfo> Candidates { get; init; }
    public IReadOnlyList<string> ActiveThemes { get; init; } = [];
    public IReadOnlyList<string> RecentSemanticEvents { get; init; } = [];
    public int BatchSize { get; init; } = 3;
    public string? CacheKey { get; init; }
}

public sealed class ActorCandidateInfo
{
    public required string Name { get; init; }
    public string? Role { get; init; }
    public bool IsInScene { get; init; }
    public required string AffinityStatus { get; init; }
    public bool? TimeOfDayMatch { get; init; }
    public IReadOnlyDictionary<string, int> KeyStats { get; init; } = new Dictionary<string, int>();
    public int? LastSpokeTurnsAgo { get; init; }
    public double BaseScore { get; init; }
    public string? AffinityDetails { get; init; }
}

public sealed class ActorSelectionResponse
{
    public bool Success { get; init; } = true;
    public string? ErrorMessage { get; init; }
    public required IReadOnlyList<string> OrderedNames { get; init; }
    public string? Reasoning { get; init; }
    public required ActorSelectionSource Source { get; init; }
    public required string RawModelOutput { get; init; }
    public required string PromptSystem { get; init; }
    public required string PromptUser { get; init; }
}
