using DreamGenClone.Domain.Administration;

namespace DreamGenClone.Application.Administration;

public interface IDatabaseBackupRepository
{
    Task<DatabaseBackup> SaveAsync(DatabaseBackup backup, CancellationToken cancellationToken = default);

    Task<DatabaseBackup?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<List<DatabaseBackup>> GetAllAsync(CancellationToken cancellationToken = default);
}