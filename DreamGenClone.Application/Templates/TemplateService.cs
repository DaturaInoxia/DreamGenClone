using DreamGenClone.Domain.Templates;
using DreamGenClone.Application.StoryAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DreamGenClone.Application.Templates;

public sealed class TemplateService : ITemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        command.Parameters.AddWithValue("$payload", SerializePayload(template));
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

        var template = new TemplateDefinition
        {
            Id = Guid.Parse(reader.GetString(0)),
            TemplateType = parsed,
            Name = reader.GetString(2),
            ImagePath = reader.IsDBNull(4) ? null : reader.GetString(4),
            UpdatedUtc = updatedUtc
        };

        var payload = reader.GetString(3);
        if (IsCharacterLikeTemplate(template.TemplateType)
            && TryDeserializeCharacterPayload(payload, out var characterPayload))
        {
            template.Content = characterPayload.Content;
            template.Gender = CharacterGenderCatalog.NormalizeForCharacter(characterPayload.Gender);
            template.Role = CharacterRoleCatalog.Normalize(characterPayload.Role);
            template.RelationTargetTemplateId = CharacterRelationCatalog.NormalizeTargetId(characterPayload.RelationTargetTemplateId);
            template.BaseStats = AdaptiveStatCatalog.NormalizeComplete(characterPayload.BaseStats);
            return template;
        }

        template.Content = payload;
        template.Gender = CharacterGenderCatalog.Unknown;
        template.Role = CharacterRoleCatalog.Unknown;
        template.BaseStats = AdaptiveStatCatalog.CreateDefaultStatMap();
        return template;
    }

    private static string SerializePayload(TemplateDefinition template)
    {
        if (!IsCharacterLikeTemplate(template.TemplateType))
        {
            return template.Content;
        }

        var payload = new CharacterTemplatePayload
        {
            Content = template.Content,
            Gender = CharacterGenderCatalog.NormalizeForCharacter(template.Gender),
            Role = CharacterRoleCatalog.Normalize(template.Role),
            RelationTargetTemplateId = CharacterRelationCatalog.NormalizeTargetId(template.RelationTargetTemplateId),
            BaseStats = AdaptiveStatCatalog.NormalizeComplete(template.BaseStats)
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static bool IsCharacterLikeTemplate(TemplateType templateType)
        => templateType == TemplateType.Character || templateType == TemplateType.Persona;

    private static bool TryDeserializeCharacterPayload(string payloadJson, out CharacterTemplatePayload payload)
    {
        payload = new CharacterTemplatePayload
        {
            Content = payloadJson,
            BaseStats = AdaptiveStatCatalog.CreateDefaultStatMap()
        };

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var content = payloadJson;
            if (doc.RootElement.TryGetProperty("content", out var contentNode)
                && contentNode.ValueKind == JsonValueKind.String)
            {
                content = contentNode.GetString() ?? string.Empty;
            }

            var stats = AdaptiveStatCatalog.CreateDefaultStatMap();
            if (doc.RootElement.TryGetProperty("baseStats", out var statsNode)
                && statsNode.ValueKind == JsonValueKind.Object)
            {
                var parsedStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in statsNode.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value))
                    {
                        parsedStats[property.Name] = value;
                    }
                }

                stats = AdaptiveStatCatalog.NormalizeComplete(parsedStats);
            }

            var gender = CharacterGenderCatalog.Unknown;
            if (doc.RootElement.TryGetProperty("gender", out var genderNode)
                && genderNode.ValueKind == JsonValueKind.String)
            {
                gender = CharacterGenderCatalog.NormalizeForCharacter(genderNode.GetString());
            }

            var role = CharacterRoleCatalog.Unknown;
            if (doc.RootElement.TryGetProperty("role", out var roleNode)
                && roleNode.ValueKind == JsonValueKind.String)
            {
                role = CharacterRoleCatalog.Normalize(roleNode.GetString());
            }

            string? relationTargetTemplateId = null;
            if (doc.RootElement.TryGetProperty("relationTargetTemplateId", out var relationNode)
                && relationNode.ValueKind == JsonValueKind.String)
            {
                relationTargetTemplateId = CharacterRelationCatalog.NormalizeTargetId(relationNode.GetString());
            }

            payload = new CharacterTemplatePayload
            {
                Content = content,
                Gender = gender,
                Role = role,
                RelationTargetTemplateId = relationTargetTemplateId,
                BaseStats = stats
            };

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class CharacterTemplatePayload
    {
        public string Content { get; set; } = string.Empty;

        public string Gender { get; set; } = CharacterGenderCatalog.Unknown;

        public string Role { get; set; } = CharacterRoleCatalog.Unknown;

        public string? RelationTargetTemplateId { get; set; }

        public Dictionary<string, int> BaseStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
