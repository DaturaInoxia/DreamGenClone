using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ProducedImageRepository : IProducedImageRepository
{
    private const string SelectSql = """
        SELECT Id, Kind, SessionId, InteractionId, BatchId, TargetRef, ReferenceKind, Status,
               ParentImageId, VisionSource, VisionText, PromptCompiled, PromptEdited, NegativePrompt,
               Seed, ModelId, EndpointId, AppliedReferencesJson, IdentityStrategy, CostJson, StoragePath,
               RefusalMode, ScoreJson, CreatedUtc, UpdatedUtc, CandidateNotes
        FROM ProducedImages
        """;

    private readonly string _connectionString;

    public ProducedImageRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task InsertAsync(ProducedImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        Require(image.Id, "Produced image id");

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ProducedImages (
                Id, Kind, SessionId, InteractionId, BatchId, TargetRef, ReferenceKind, Status,
                ParentImageId, VisionSource, VisionText, PromptCompiled, PromptEdited, NegativePrompt,
                Seed, ModelId, EndpointId, AppliedReferencesJson, IdentityStrategy, CostJson, StoragePath,
                RefusalMode, ScoreJson, CreatedUtc, UpdatedUtc, CandidateNotes)
            VALUES (
                $id, $kind, $sessionId, $interactionId, $batchId, $targetRef, $referenceKind, $status,
                $parentImageId, $visionSource, $visionText, $promptCompiled, $promptEdited, $negativePrompt,
                $seed, $modelId, $endpointId, $appliedReferencesJson, $identityStrategy, $costJson, $storagePath,
                $refusalMode, $scoreJson, $createdUtc, $updatedUtc, $candidateNotes);
            """;
        AddParameters(command, image);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(ProducedImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        Require(image.Id, "Produced image id");

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ProducedImages SET
                Kind = $kind, SessionId = $sessionId, InteractionId = $interactionId, BatchId = $batchId,
                TargetRef = $targetRef, ReferenceKind = $referenceKind, Status = $status,
                ParentImageId = $parentImageId, VisionSource = $visionSource, VisionText = $visionText,
                PromptCompiled = $promptCompiled, PromptEdited = $promptEdited, NegativePrompt = $negativePrompt,
                Seed = $seed, ModelId = $modelId, EndpointId = $endpointId,
                AppliedReferencesJson = $appliedReferencesJson, IdentityStrategy = $identityStrategy,
                CostJson = $costJson, StoragePath = $storagePath, RefusalMode = $refusalMode,
                ScoreJson = $scoreJson, CreatedUtc = $createdUtc, UpdatedUtc = $updatedUtc,
                CandidateNotes = $candidateNotes
            WHERE Id = $id;
            """;
        AddParameters(command, image);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException($"Produced image '{image.Id}' was not found for update.");
        }
    }

    public async Task<ProducedImage?> GetAsync(string imageId, CancellationToken cancellationToken = default)
    {
        Require(imageId, "Produced image id");
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", imageId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadImage(reader) : null;
    }

    public Task<IReadOnlyList<ProducedImage>> ListBySessionAsync(
        string sessionId, string? interactionId = null, CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Session id");
        return ListAsync("SessionId = $sessionId AND ($interactionId IS NULL OR InteractionId = $interactionId)",
            command =>
            {
                command.Parameters.AddWithValue("$sessionId", sessionId.Trim());
                command.Parameters.AddWithValue("$interactionId", (object?)interactionId?.Trim() ?? DBNull.Value);
            }, cancellationToken);
    }

    public Task<IReadOnlyList<ProducedImage>> ListByBatchAsync(
        string batchId, CancellationToken cancellationToken = default)
    {
        Require(batchId, "Batch id");
        return ListAsync("BatchId = $batchId", command => command.Parameters.AddWithValue("$batchId", batchId.Trim()),
            cancellationToken);
    }

    public Task<IReadOnlyList<ProducedImage>> ListByParentAsync(
        string parentImageId, CancellationToken cancellationToken = default)
    {
        Require(parentImageId, "Parent image id");
        return ListAsync("ParentImageId = $parentImageId", command => command.Parameters.AddWithValue("$parentImageId", parentImageId.Trim()),
            cancellationToken);
    }

    public Task<IReadOnlyList<ProducedImage>> ListByStatusAsync(
        ProducedImageStatus status, CancellationToken cancellationToken = default) =>
        ListAsync("Status = $status", command => command.Parameters.AddWithValue("$status", status.ToString()),
            cancellationToken);

    public async Task<ProducedImagePage> QueryAsync(
        ProducedImageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Take <= 0)
        {
            throw new InvalidOperationException("Produced image query Take must be positive.");
        }

        var predicates = new List<string>();
        var addParameters = new Action<SqliteCommand>(command => { });

        void AddFacet(string predicate, string parameterName, object value)
        {
            predicates.Add(predicate);
            var previousAddParameters = addParameters;
            addParameters = command =>
            {
                previousAddParameters(command);
                command.Parameters.AddWithValue(parameterName, value);
            };
        }

        if (query.Kind is { } kind) AddFacet("Kind = $kind", "$kind", kind.ToString());
        if (query.Status is { } status) AddFacet("Status = $status", "$status", status.ToString());
        if (query.ReferenceKind is { } referenceKind)
        {
            AddFacet("ReferenceKind = $referenceKind", "$referenceKind", referenceKind.ToString());
        }

        if (query.BatchId is { } batchId) AddFacet("BatchId = $batchId", "$batchId", batchId.Trim());
        if (query.TargetRef is { } targetRef) AddFacet("TargetRef = $targetRef", "$targetRef", targetRef.Trim());
        if (query.ModelId is { } modelId) AddFacet("ModelId = $modelId", "$modelId", modelId.Trim());

        var whereClause = predicates.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", predicates)}";
        var skip = Math.Max(0, query.Skip);

        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM ProducedImages{whereClause};";
        addParameters(countCommand);
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var pageCommand = connection.CreateCommand();
        pageCommand.CommandText = $"{SelectSql}{whereClause} ORDER BY CreatedUtc DESC, Id ASC LIMIT $take OFFSET $skip;";
        addParameters(pageCommand);
        pageCommand.Parameters.AddWithValue("$take", query.Take);
        pageCommand.Parameters.AddWithValue("$skip", skip);

        var items = new List<ProducedImage>();
        await using var reader = await pageCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadImage(reader));

        return new ProducedImagePage { Items = items, TotalCount = totalCount };
    }

    private async Task<IReadOnlyList<ProducedImage>> ListAsync(
        string predicate, Action<SqliteCommand> addParameters, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE {predicate} ORDER BY CreatedUtc ASC, Id ASC;";
        addParameters(command);
        var results = new List<ProducedImage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadImage(reader));
        return results;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return connection;
    }

    private static ProducedImage ReadImage(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        return new ProducedImage
        {
            Id = id,
            Kind = ParseEnum<ProducedImageKind>(reader.GetString(1), id),
            SessionId = ReadNullable(reader, 2),
            InteractionId = ReadNullable(reader, 3),
            BatchId = ReadNullable(reader, 4),
            TargetRef = ReadNullable(reader, 5),
            ReferenceKind = reader.IsDBNull(6) ? null : ParseEnum<ProducedImageReferenceKind>(reader.GetString(6), id),
            Status = ParseEnum<ProducedImageStatus>(reader.GetString(7), id),
            ParentImageId = ReadNullable(reader, 8),
            VisionSource = ParseEnum<ProducedImageVisionSource>(reader.GetString(9), id),
            VisionText = ReadNullable(reader, 10),
            PromptCompiled = ReadNullable(reader, 11),
            PromptEdited = ReadNullable(reader, 12),
            NegativePrompt = ReadNullable(reader, 13),
            Seed = reader.IsDBNull(14) ? null : reader.GetInt64(14),
            ModelId = ReadNullable(reader, 15),
            EndpointId = ReadNullable(reader, 16),
            AppliedReferencesJson = ReadNullable(reader, 17),
            IdentityStrategy = ReadNullable(reader, 18),
            CostJson = ReadNullable(reader, 19),
            StoragePath = ReadNullable(reader, 20),
            RefusalMode = ParseEnum<SceneImageRefusalMode>(reader.GetString(21), id),
            ScoreJson = ReadNullable(reader, 22),
            CreatedUtc = reader.GetString(23),
            UpdatedUtc = reader.GetString(24),
            CandidateNotes = ReadNullable(reader, 25)
        };
    }

    private static void AddParameters(SqliteCommand command, ProducedImage image)
    {
        command.Parameters.AddWithValue("$id", image.Id.Trim());
        command.Parameters.AddWithValue("$kind", image.Kind.ToString());
        command.Parameters.AddWithValue("$sessionId", (object?)image.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$interactionId", (object?)image.InteractionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$batchId", (object?)image.BatchId ?? DBNull.Value);
        command.Parameters.AddWithValue("$targetRef", (object?)image.TargetRef ?? DBNull.Value);
        command.Parameters.AddWithValue("$referenceKind", (object?)image.ReferenceKind?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", image.Status.ToString());
        command.Parameters.AddWithValue("$parentImageId", (object?)image.ParentImageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$visionSource", image.VisionSource.ToString());
        command.Parameters.AddWithValue("$visionText", (object?)image.VisionText ?? DBNull.Value);
        command.Parameters.AddWithValue("$promptCompiled", (object?)image.PromptCompiled ?? DBNull.Value);
        command.Parameters.AddWithValue("$promptEdited", (object?)image.PromptEdited ?? DBNull.Value);
        command.Parameters.AddWithValue("$negativePrompt", (object?)image.NegativePrompt ?? DBNull.Value);
        command.Parameters.AddWithValue("$seed", (object?)image.Seed ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelId", (object?)image.ModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$endpointId", (object?)image.EndpointId ?? DBNull.Value);
        command.Parameters.AddWithValue("$appliedReferencesJson", (object?)image.AppliedReferencesJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$identityStrategy", (object?)image.IdentityStrategy ?? DBNull.Value);
        command.Parameters.AddWithValue("$costJson", (object?)image.CostJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$storagePath", (object?)image.StoragePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$refusalMode", image.RefusalMode.ToString());
        command.Parameters.AddWithValue("$scoreJson", (object?)image.ScoreJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", image.CreatedUtc);
        command.Parameters.AddWithValue("$updatedUtc", image.UpdatedUtc);
        command.Parameters.AddWithValue("$candidateNotes", (object?)image.CandidateNotes ?? DBNull.Value);
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ProducedImages (
                Id TEXT PRIMARY KEY,
                Kind TEXT NOT NULL,
                SessionId TEXT NULL,
                InteractionId TEXT NULL,
                BatchId TEXT NULL,
                TargetRef TEXT NULL,
                ReferenceKind TEXT NULL,
                Status TEXT NOT NULL,
                ParentImageId TEXT NULL,
                VisionSource TEXT NOT NULL,
                VisionText TEXT NULL,
                PromptCompiled TEXT NULL,
                PromptEdited TEXT NULL,
                NegativePrompt TEXT NULL,
                Seed INTEGER NULL,
                ModelId TEXT NULL,
                EndpointId TEXT NULL,
                AppliedReferencesJson TEXT NULL,
                IdentityStrategy TEXT NULL,
                CostJson TEXT NULL,
                StoragePath TEXT NULL,
                RefusalMode TEXT NOT NULL,
                ScoreJson TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                CandidateNotes TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ProducedImages_SessionInteraction
                ON ProducedImages (SessionId, InteractionId);
            CREATE INDEX IF NOT EXISTS IX_ProducedImages_Batch
                ON ProducedImages (BatchId);
            CREATE INDEX IF NOT EXISTS IX_ProducedImages_Parent
                ON ProducedImages (ParentImageId);
            CREATE INDEX IF NOT EXISTS IX_ProducedImages_Status
                ON ProducedImages (Status);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var migrationCommand = connection.CreateCommand();
        migrationCommand.CommandText = "PRAGMA table_info(ProducedImages);";
        var hasCandidateNotes = false;
        await using (var reader = await migrationCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "CandidateNotes", StringComparison.OrdinalIgnoreCase))
                {
                    hasCandidateNotes = true;
                    break;
                }
            }
        }

        if (!hasCandidateNotes)
        {
            await using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE ProducedImages ADD COLUMN CandidateNotes TEXT NULL;";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string? ReadNullable(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static TEnum ParseEnum<TEnum>(string value, string id) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, true, out var parsed)) return parsed;
        throw new InvalidOperationException($"Invalid {typeof(TEnum).Name} value '{value}' for ProducedImages record '{id}'.");
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }
}
