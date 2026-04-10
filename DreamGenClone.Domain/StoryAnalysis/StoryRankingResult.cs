namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class StoryRankingResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ParsedStoryId { get; set; } = string.Empty;

    public string ProfileId { get; set; } = string.Empty;

    public string ThemeSnapshotJson { get; set; } = "[]";

    public string ThemeDetectionsJson { get; set; } = "[]";

    public double Score { get; set; }

    public bool IsDisqualified { get; set; }

    public string ThemeVerificationStatusJson { get; set; } = "{}";

    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
