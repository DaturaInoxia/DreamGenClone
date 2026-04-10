namespace DreamGenClone.Domain.ModelManager;

public sealed class HealthCheckResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public HealthCheckEntityType EntityType { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public string Message { get; set; } = string.Empty;
    public string CheckedUtc { get; set; } = DateTime.UtcNow.ToString("o");
}
