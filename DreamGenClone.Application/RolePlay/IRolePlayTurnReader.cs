using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IRolePlayTurnReader
{
    Task<RolePlayTurn?> GetTurnAsync(
        string sessionId,
        string turnId,
        CancellationToken cancellationToken = default);
}