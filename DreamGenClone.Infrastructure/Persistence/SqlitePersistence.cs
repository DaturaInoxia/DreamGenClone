using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.Persistence;

public sealed class SqlitePersistence : ISqlitePersistence
{
    private readonly PersistenceOptions _options;
    private readonly ILogger<SqlitePersistence> _logger;

    public SqlitePersistence(IOptions<PersistenceOptions> options, ILogger<SqlitePersistence> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder(_options.ConnectionString);
        if (!string.IsNullOrWhiteSpace(connectionStringBuilder.DataSource))
        {
            var dataSourcePath = Path.GetFullPath(connectionStringBuilder.DataSource);
            var dataDirectory = Path.GetDirectoryName(dataSourcePath);
            if (!string.IsNullOrWhiteSpace(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Sessions (
                Id TEXT PRIMARY KEY,
                SessionType TEXT NOT NULL,
                Name TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Templates (
                Id TEXT PRIMARY KEY,
                TemplateType TEXT NOT NULL,
                Name TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                ImagePath TEXT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("SQLite persistence initialized using {ConnectionString}", _options.ConnectionString);
    }
}
