using DreamGenClone.Domain.Templates;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Application.Templates;

public sealed class TemplateService : ITemplateService
{
    private readonly string _connectionString;
    private readonly ILogger<TemplateService> _logger;

    public TemplateService(IConfiguration configuration, ILogger<TemplateService> logger)
    {
        _connectionString = configuration["Persistence:ConnectionString"] ?? "Data Source=data/dreamgenclone.db";
        _logger = logger;
    }

    public async Task<IReadOnlyList<TemplateDefinition>> GetAllAsync(TemplateType? templateType = null, CancellationToken cancellationToken = default)
    {
        var results = new List<TemplateDefinition>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = templateType is null
            ? "SELECT Id, TemplateType, Name, PayloadJson, ImagePath, UpdatedUtc FROM Templates ORDER BY UpdatedUtc DESC"
            : "SELECT Id, TemplateType, Name, PayloadJson, ImagePath, UpdatedUtc FROM Templates WHERE TemplateType = $type ORDER BY UpdatedUtc DESC";

        if (templateType is not null)
        {
            command.Parameters.AddWithValue("$type", templateType.ToString());
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(MapTemplate(reader));
        }

        return results;
    }

    public async Task<TemplateDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, TemplateType, Name, PayloadJson, ImagePath, UpdatedUtc FROM Templates WHERE Id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapTemplate(reader);
    }

    public async Task<TemplateDefinition> SaveAsync(TemplateDefinition template, CancellationToken cancellationToken = default)
    {
        template.UpdatedUtc = DateTime.UtcNow;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Templates (Id, TemplateType, Name, PayloadJson, ImagePath, UpdatedUtc)
            VALUES ($id, $type, $name, $payload, $imagePath, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                TemplateType = excluded.TemplateType,
                Name = excluded.Name,
                PayloadJson = excluded.PayloadJson,
                ImagePath = excluded.ImagePath,
                UpdatedUtc = excluded.UpdatedUtc;
            """;

        command.Parameters.AddWithValue("$id", template.Id.ToString());
        command.Parameters.AddWithValue("$type", template.TemplateType.ToString());
        command.Parameters.AddWithValue("$name", template.Name);
        command.Parameters.AddWithValue("$payload", template.Content);
        command.Parameters.AddWithValue("$imagePath", (object?)template.ImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", template.UpdatedUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Template {TemplateId} saved as type {TemplateType}", template.Id, template.TemplateType);

        return template;
    }

    public async Task UpdateImagePathAsync(Guid id, string imagePath, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Templates SET ImagePath = $imagePath, UpdatedUtc = $updatedUtc WHERE Id = $id";
        command.Parameters.AddWithValue("$imagePath", imagePath);
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Template {TemplateId} image path updated", id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Templates WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Template {TemplateId} deleted", id);
    }

    private static TemplateDefinition MapTemplate(SqliteDataReader reader)
    {
        var templateTypeText = reader.GetString(1);
        var parsed = Enum.TryParse<TemplateType>(templateTypeText, out var templateType)
            ? templateType
            : TemplateType.Object;

        var updatedUtc = DateTime.TryParse(reader.GetString(5), out var parsedDate)
            ? parsedDate
            : DateTime.UtcNow;

        return new TemplateDefinition
        {
            Id = Guid.Parse(reader.GetString(0)),
            TemplateType = parsed,
            Name = reader.GetString(2),
            Content = reader.GetString(3),
            ImagePath = reader.IsDBNull(4) ? null : reader.GetString(4),
            UpdatedUtc = updatedUtc
        };
    }
}
