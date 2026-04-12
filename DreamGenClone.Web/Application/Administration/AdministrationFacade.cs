using DreamGenClone.Application.Administration;
using DreamGenClone.Application.Sessions;
using DreamGenClone.Domain.Administration;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.Administration;

public sealed class AdministrationFacade
{
    private readonly IDatabaseBackupRepository _backupRepository;
    private readonly IAutoSaveCoordinator _autoSaveCoordinator;
    private readonly PersistenceOptions _persistenceOptions;
    private readonly ISqlitePersistence _sqlitePersistence;
    private readonly ILogger<AdministrationFacade> _logger;

    public AdministrationFacade(
        IDatabaseBackupRepository backupRepository,
        IAutoSaveCoordinator autoSaveCoordinator,
        IOptions<PersistenceOptions> persistenceOptions,
        ISqlitePersistence sqlitePersistence,
        ILogger<AdministrationFacade> logger)
    {
        _backupRepository = backupRepository;
        _autoSaveCoordinator = autoSaveCoordinator;
        _persistenceOptions = persistenceOptions.Value;
        _sqlitePersistence = sqlitePersistence;
        _logger = logger;
    }

    public Task<List<DatabaseBackup>> GetDatabaseBackupsAsync(CancellationToken cancellationToken = default)
        => _backupRepository.GetAllAsync(cancellationToken);

    public async Task<DatabaseBackup> CreateDatabaseBackupAsync(string? displayName, CancellationToken cancellationToken = default)
    {
        await _autoSaveCoordinator.FlushAsync(cancellationToken);

        var backup = await _sqlitePersistence.CreateDatabaseBackupAsync(displayName, cancellationToken);
        await _backupRepository.SaveAsync(backup, cancellationToken);

        _logger.LogInformation("Database backup created: {BackupId} ({FileName})", backup.Id, backup.FileName);
        return backup;
    }

    public async Task<(DatabaseBackup Backup, string FilePath)?> GetBackupDownloadAsync(string backupId, CancellationToken cancellationToken = default)
    {
        var backup = await _backupRepository.GetByIdAsync(backupId, cancellationToken);
        if (backup is null)
        {
            return null;
        }

        var filePath = ResolveBackupPath(backup.RelativePath);
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Backup file missing for metadata record {BackupId}: {FilePath}", backupId, filePath);
            return null;
        }

        return (backup, filePath);
    }

    private string ResolveBackupPath(string relativePath)
    {
        var builder = new SqliteConnectionStringBuilder(_persistenceOptions.ConnectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new InvalidOperationException("Persistence connection string does not contain a SQLite data source.");
        }

        var databasePath = Path.GetFullPath(builder.DataSource);
        var dataDirectory = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("Could not resolve the SQLite data directory.");
        var candidate = Path.GetFullPath(Path.Combine(dataDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!candidate.StartsWith(dataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Backup path resolved outside the data directory.");
        }

        return candidate;
    }
}