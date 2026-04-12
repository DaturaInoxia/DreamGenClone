using DreamGenClone.Application.Administration;
using DreamGenClone.Domain.Administration;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.Administration;

public sealed class DatabaseBackupRepository : IDatabaseBackupRepository
{
    private readonly PersistenceOptions _options;

    public DatabaseBackupRepository(IOptions<PersistenceOptions> options)
    {
        _options = options.Value;
    }

    public async Task<DatabaseBackup> SaveAsync(DatabaseBackup backup, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backup);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DatabaseBackups (Id, DisplayName, FileName, RelativePath, FileSizeBytes, TriggeredBy, CreatedUtc)
            VALUES ($id, $displayName, $fileName, $relativePath, $fileSizeBytes, $triggeredBy, $createdUtc)
            ON CONFLICT(Id) DO UPDATE SET
                DisplayName = $displayName,
                FileName = $fileName,
                RelativePath = $relativePath,
                FileSizeBytes = $fileSizeBytes,
                TriggeredBy = $triggeredBy,
                CreatedUtc = $createdUtc;
            """;

        command.Parameters.AddWithValue("$id", backup.Id);
        command.Parameters.AddWithValue("$displayName", backup.DisplayName);
        command.Parameters.AddWithValue("$fileName", backup.FileName);
        command.Parameters.AddWithValue("$relativePath", backup.RelativePath);
        command.Parameters.AddWithValue("$fileSizeBytes", backup.FileSizeBytes);
        command.Parameters.AddWithValue("$triggeredBy", backup.TriggeredBy);
        command.Parameters.AddWithValue("$createdUtc", backup.CreatedUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        return backup;
    }

    public async Task<DatabaseBackup?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, DisplayName, FileName, RelativePath, FileSizeBytes, TriggeredBy, CreatedUtc FROM DatabaseBackups WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadBackup(reader);
    }

    public async Task<List<DatabaseBackup>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, DisplayName, FileName, RelativePath, FileSizeBytes, TriggeredBy, CreatedUtc FROM DatabaseBackups ORDER BY CreatedUtc DESC";

        var backups = new List<DatabaseBackup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            backups.Add(ReadBackup(reader));
        }

        return backups;
    }

    private static DatabaseBackup ReadBackup(SqliteDataReader reader)
    {
        return new DatabaseBackup
        {
            Id = reader.GetString(0),
            DisplayName = reader.GetString(1),
            FileName = reader.GetString(2),
            RelativePath = reader.GetString(3),
            FileSizeBytes = reader.GetInt64(4),
            TriggeredBy = reader.GetString(5),
            CreatedUtc = DateTime.TryParse(reader.GetString(6), out var createdUtc) ? createdUtc : DateTime.UtcNow
        };
    }
}