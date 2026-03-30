namespace DreamGenClone.Domain.StoryParser;

public sealed class ParsedStoryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string SourceUrl { get; set; } = string.Empty;

    public string SourceDomain { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Author { get; set; }

    public DateTime ParsedUtc { get; set; } = DateTime.UtcNow;

    public int PageCount { get; set; }

    public string CombinedText { get; set; } = string.Empty;

    public string StructuredPayloadJson { get; set; } = "[]";

    public ParseStatus ParseStatus { get; set; } = ParseStatus.Failed;

    public string DiagnosticsSummaryJson { get; set; } = "{}";

    public bool IsArchived { get; set; }
}
