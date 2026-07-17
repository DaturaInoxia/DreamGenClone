namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Payload for the background location detection job.
/// Interactions, location names, and character names are included inline
/// to avoid a race condition: the job may run before the session's PayloadJson
/// save completes, causing stale interaction reads and duplicate LLM calls.
/// </summary>
public sealed class LocationDetectionJobPayload
{
    public required string SessionId { get; init; }
    public required IReadOnlyList<string> RecentInteractionSummaries { get; init; }
    public required IReadOnlyList<string> ScenarioLocationNames { get; init; }
    public required IReadOnlyList<string> CharacterNames { get; init; }
    public string? PreviousLocation { get; init; }
}
