namespace DreamGenClone.Web.Application.BackgroundJobs;

public sealed class BackgroundJobEnvelope
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("N");

    public string JobType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public string? DedupeKey { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
