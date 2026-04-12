namespace DreamGenClone.Application.RolePlay;

public interface IRolePlayDiagnosticsService
{
    Task<RolePlayV2DiagnosticsSnapshot> GetSnapshotAsync(string sessionId, string? correlationId = null, CancellationToken cancellationToken = default);
}
