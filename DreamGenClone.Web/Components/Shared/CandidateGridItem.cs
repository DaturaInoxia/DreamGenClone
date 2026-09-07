namespace DreamGenClone.Components.Shared;

public sealed record CandidateGridItem(
    string Id,
    string? ImagePath,
    string Status,
    string? Subtitle);