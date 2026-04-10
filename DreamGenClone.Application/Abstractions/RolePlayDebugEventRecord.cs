namespace DreamGenClone.Application.Abstractions;

public sealed class RolePlayDebugEventRecord
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");

    public string SessionId { get; set; } = string.Empty;

    public string? CorrelationId { get; set; }

    public string? InteractionId { get; set; }

    public string EventKind { get; set; } = "General";

    public string Severity { get; set; } = "Info";

    public string? ActorName { get; set; }

    public string? ModelIdentifier { get; set; }

    public string? ProviderName { get; set; }

    public int? DurationMs { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string MetadataJson { get; set; } = "{}";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
