namespace DreamGenClone.Domain.Administration;

public sealed class DatabaseBackup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public string TriggeredBy { get; set; } = "manual";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}