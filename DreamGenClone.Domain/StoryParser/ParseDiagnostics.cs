namespace DreamGenClone.Domain.StoryParser;

public sealed class ParseDiagnostics
{
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    public DateTime CompletedUtc { get; set; } = DateTime.UtcNow;

    public List<DiagnosticItem> Errors { get; set; } = [];

    public List<DiagnosticItem> Warnings { get; set; } = [];
}

public sealed class DiagnosticItem
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Severity { get; set; } = "Error";

    public string Stage { get; set; } = "Unknown";

    public string? PageUrl { get; set; }
}
