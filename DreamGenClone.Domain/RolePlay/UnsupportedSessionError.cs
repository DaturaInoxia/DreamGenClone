namespace DreamGenClone.Domain.RolePlay;

public sealed class UnsupportedSessionError
{
    public string ErrorCode { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? DetectedSchemaVersion { get; set; }
    public List<string> MissingCanonicalStats { get; set; } = [];
    public string RecoveryGuidance { get; set; } = string.Empty;
    public DateTime EmittedUtc { get; set; } = DateTime.UtcNow;
}
