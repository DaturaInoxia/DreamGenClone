using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class CompiledMediaBriefRepository : ICompiledMediaBriefRepository, IApprovedMediaDerivativeRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _connectionString;

    public CompiledMediaBriefRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task CreateAsync(CompiledMediaBrief brief, CancellationToken cancellationToken = default)
    {
        CompiledMediaContractValidator.ValidateBrief(brief);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CompiledMediaBriefs (
                Id, MediaKind, TargetProfileId, TargetProfileVersion, FamilyKey, CompilerKey,
                CompilerVersion, ProviderRequestContractVersion, CatalogueId, BeatId,
                BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion,
                MomentId, MomentEnrichmentId, MomentEnrichmentRevision, CanonicalSourceIdsJson,
                SemanticInputSnapshotJson, ProviderRequestSnapshotJson, RequiredIntentCoverageJson,
                Status, ErrorCode, ErrorMessage, CreatedUtc, CompletedUtc)
            VALUES (
                $id, $mediaKind, $profileId, $profileVersion, $familyKey, $compilerKey,
                $compilerVersion, $requestVersion, $catalogueId, $beatId,
                $planId, $planVersion, $momentSetId, $momentSetVersion,
                $momentId, $enrichmentId, $enrichmentRevision, $sourceIds,
                $semantic, $request, $coverage, $status, $errorCode, $errorMessage,
                $createdUtc, $completedUtc);
            """;
        AddBriefParameters(command, brief);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException($"Compiled media brief '{brief.Id}' already exists and is immutable.", exception);
        }
    }

    public async Task<CompiledMediaBrief?> GetAsync(string briefId, CancellationToken cancellationToken = default)
    {
        Require(briefId, "Brief id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateBriefSelect(connection);
        command.CommandText += " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", briefId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBrief(reader) : null;
    }

    public Task<IReadOnlyList<CompiledMediaBrief>> ListByMomentEnrichmentAsync(
        string momentEnrichmentId,
        CancellationToken cancellationToken = default) =>
        ListBriefsAsync("MomentEnrichmentId", momentEnrichmentId, cancellationToken);

    public Task<IReadOnlyList<CompiledMediaBrief>> ListByBeatProductionPlanAsync(
        string beatProductionPlanId,
        CancellationToken cancellationToken = default) =>
        ListBriefsAsync("BeatProductionPlanId", beatProductionPlanId, cancellationToken);

    async Task IApprovedMediaDerivativeRepository.CreateAsync(
        ApprovedMediaDerivative derivative,
        CancellationToken cancellationToken)
    {
        CompiledMediaContractValidator.ValidateDerivative(derivative);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ApprovedMediaDerivatives (
                Id, Version, MediaKind, SourceBriefId, SourceBriefProfileVersion, SourceCueIdsJson,
                AssetId, AssetChecksum, RealizedAlignmentJson, ApprovedUtc, CreatedUtc)
            VALUES ($id, $version, $mediaKind, $briefId, $profileVersion, $cueIds,
                $assetId, $checksum, $alignment, $approvedUtc, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", derivative.Id.Trim());
        command.Parameters.AddWithValue("$version", derivative.Version);
        command.Parameters.AddWithValue("$mediaKind", derivative.MediaKind.ToString());
        command.Parameters.AddWithValue("$briefId", derivative.SourceBriefId.Trim());
        command.Parameters.AddWithValue("$profileVersion", derivative.SourceBriefProfileVersion.Trim());
        command.Parameters.AddWithValue("$cueIds", JsonSerializer.Serialize(derivative.SourceCueIds, JsonOptions));
        command.Parameters.AddWithValue("$assetId", derivative.AssetId.Trim());
        command.Parameters.AddWithValue("$checksum", derivative.AssetChecksum.Trim());
        command.Parameters.AddWithValue("$alignment", derivative.RealizedAlignment is null
            ? DBNull.Value
            : JsonSerializer.Serialize(derivative.RealizedAlignment, JsonOptions));
        command.Parameters.AddWithValue("$approvedUtc", FormatUtc(derivative.ApprovedUtc));
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(derivative.CreatedUtc));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException($"Approved media derivative '{derivative.Id}' already exists and is immutable.", exception);
        }
    }

    async Task<ApprovedMediaDerivative?> IApprovedMediaDerivativeRepository.GetAsync(
        string derivativeId,
        CancellationToken cancellationToken)
    {
        Require(derivativeId, "Derivative id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Version, MediaKind, SourceBriefId, SourceBriefProfileVersion, SourceCueIdsJson,
                   AssetId, AssetChecksum, RealizedAlignmentJson, ApprovedUtc, CreatedUtc
            FROM ApprovedMediaDerivatives WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", derivativeId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var derivative = new ApprovedMediaDerivative(
            reader.GetString(0), reader.GetInt32(1), ParseEnum<MediaProductionKind>(reader.GetString(2), derivativeId),
            reader.GetString(3), reader.GetString(4), Deserialize<IReadOnlyList<string>>(reader.GetString(5), "SourceCueIdsJson", derivativeId),
            reader.GetString(6), reader.GetString(7), reader.IsDBNull(8) ? null : Deserialize<RealizedMediaAlignment>(reader.GetString(8), "RealizedAlignmentJson", derivativeId),
            ParseUtc(reader.GetString(9), derivativeId, "ApprovedUtc"), ParseUtc(reader.GetString(10), derivativeId, "CreatedUtc"));
        CompiledMediaContractValidator.ValidateDerivative(derivative);
        return derivative;
    }

    private async Task<IReadOnlyList<CompiledMediaBrief>> ListBriefsAsync(
        string column,
        string value,
        CancellationToken cancellationToken)
    {
        Require(value, column);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateBriefSelect(connection);
        command.CommandText += $" WHERE {column} = $value ORDER BY CreatedUtc, Id;";
        command.Parameters.AddWithValue("$value", value.Trim());
        var results = new List<CompiledMediaBrief>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadBrief(reader));
        return results;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return connection;
    }

    private static SqliteCommand CreateBriefSelect(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, MediaKind, TargetProfileId, TargetProfileVersion, FamilyKey, CompilerKey,
                   CompilerVersion, ProviderRequestContractVersion, CatalogueId, BeatId,
                   BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion,
                   MomentId, MomentEnrichmentId, MomentEnrichmentRevision, CanonicalSourceIdsJson,
                   SemanticInputSnapshotJson, ProviderRequestSnapshotJson, RequiredIntentCoverageJson,
                   Status, ErrorCode, ErrorMessage, CreatedUtc, CompletedUtc
            FROM CompiledMediaBriefs
            """;
        return command;
    }

    private static CompiledMediaBrief ReadBrief(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        var brief = new CompiledMediaBrief(
            id, ParseEnum<MediaProductionKind>(reader.GetString(1), id), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            new CompiledMediaLineage(reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetInt32(11),
                reader.GetString(12), reader.GetInt32(13), reader.GetString(14), reader.GetString(15), reader.GetInt32(16)),
            Deserialize<IReadOnlyList<string>>(reader.GetString(17), "CanonicalSourceIdsJson", id),
            reader.GetString(18), reader.GetString(19), reader.GetString(20),
            ParseEnum<MediaCompilerStatus>(reader.GetString(21), id),
            reader.IsDBNull(22) ? null : reader.GetString(22), reader.IsDBNull(23) ? null : reader.GetString(23),
            ParseUtc(reader.GetString(24), id, "CreatedUtc"), ParseUtc(reader.GetString(25), id, "CompletedUtc"));
        CompiledMediaContractValidator.ValidateBrief(brief);
        return brief;
    }

    private static void AddBriefParameters(SqliteCommand command, CompiledMediaBrief brief)
    {
        command.Parameters.AddWithValue("$id", brief.Id.Trim());
        command.Parameters.AddWithValue("$mediaKind", brief.MediaKind.ToString());
        command.Parameters.AddWithValue("$profileId", brief.TargetProfileId.Trim());
        command.Parameters.AddWithValue("$profileVersion", brief.TargetProfileVersion.Trim());
        command.Parameters.AddWithValue("$familyKey", brief.FamilyKey.Trim());
        command.Parameters.AddWithValue("$compilerKey", brief.CompilerKey.Trim());
        command.Parameters.AddWithValue("$compilerVersion", brief.CompilerVersion.Trim());
        command.Parameters.AddWithValue("$requestVersion", brief.ProviderRequestContractVersion.Trim());
        command.Parameters.AddWithValue("$catalogueId", brief.Lineage.CatalogueId.Trim());
        command.Parameters.AddWithValue("$beatId", brief.Lineage.BeatId.Trim());
        command.Parameters.AddWithValue("$planId", brief.Lineage.BeatProductionPlanId.Trim());
        command.Parameters.AddWithValue("$planVersion", brief.Lineage.BeatProductionPlanVersion);
        command.Parameters.AddWithValue("$momentSetId", brief.Lineage.MomentSetId.Trim());
        command.Parameters.AddWithValue("$momentSetVersion", brief.Lineage.MomentSetVersion);
        command.Parameters.AddWithValue("$momentId", brief.Lineage.MomentId.Trim());
        command.Parameters.AddWithValue("$enrichmentId", brief.Lineage.MomentEnrichmentId.Trim());
        command.Parameters.AddWithValue("$enrichmentRevision", brief.Lineage.MomentEnrichmentRevision);
        command.Parameters.AddWithValue("$sourceIds", JsonSerializer.Serialize(brief.CanonicalSourceIds, JsonOptions));
        command.Parameters.AddWithValue("$semantic", brief.SemanticInputSnapshotJson);
        command.Parameters.AddWithValue("$request", brief.ProviderRequestSnapshotJson);
        command.Parameters.AddWithValue("$coverage", brief.RequiredIntentCoverageJson);
        command.Parameters.AddWithValue("$status", brief.Status.ToString());
        command.Parameters.AddWithValue("$errorCode", (object?)brief.ErrorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)brief.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(brief.CreatedUtc));
        command.Parameters.AddWithValue("$completedUtc", FormatUtc(brief.CompletedUtc));
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CompiledMediaBriefs (
                Id TEXT PRIMARY KEY, MediaKind TEXT NOT NULL, TargetProfileId TEXT NOT NULL,
                TargetProfileVersion TEXT NOT NULL, FamilyKey TEXT NOT NULL, CompilerKey TEXT NOT NULL,
                CompilerVersion TEXT NOT NULL, ProviderRequestContractVersion TEXT NOT NULL,
                CatalogueId TEXT NOT NULL, BeatId TEXT NOT NULL, BeatProductionPlanId TEXT NOT NULL,
                BeatProductionPlanVersion INTEGER NOT NULL, MomentSetId TEXT NOT NULL,
                MomentSetVersion INTEGER NOT NULL, MomentId TEXT NOT NULL, MomentEnrichmentId TEXT NOT NULL,
                MomentEnrichmentRevision INTEGER NOT NULL, CanonicalSourceIdsJson TEXT NOT NULL,
                SemanticInputSnapshotJson TEXT NOT NULL, ProviderRequestSnapshotJson TEXT NOT NULL,
                RequiredIntentCoverageJson TEXT NOT NULL, Status TEXT NOT NULL, ErrorCode TEXT NULL,
                ErrorMessage TEXT NULL, CreatedUtc TEXT NOT NULL, CompletedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CompiledMediaBriefs_Enrichment
                ON CompiledMediaBriefs (MomentEnrichmentId, CreatedUtc);
            CREATE INDEX IF NOT EXISTS IX_CompiledMediaBriefs_Plan
                ON CompiledMediaBriefs (BeatProductionPlanId, CreatedUtc);
            CREATE TABLE IF NOT EXISTS ApprovedMediaDerivatives (
                Id TEXT PRIMARY KEY, Version INTEGER NOT NULL, MediaKind TEXT NOT NULL,
                SourceBriefId TEXT NOT NULL, SourceBriefProfileVersion TEXT NOT NULL,
                SourceCueIdsJson TEXT NOT NULL, AssetId TEXT NOT NULL, AssetChecksum TEXT NOT NULL,
                RealizedAlignmentJson TEXT NULL, ApprovedUtc TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
                UNIQUE (SourceBriefId, Version)
            );
            CREATE INDEX IF NOT EXISTS IX_ApprovedMediaDerivatives_SourceBrief
                ON ApprovedMediaDerivatives (SourceBriefId, Version);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static T Deserialize<T>(string json, string label, string id)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException($"{label} for record '{id}' cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{label} for record '{id}' contains invalid JSON.", exception);
        }
    }

    private static T ParseEnum<T>(string value, string id) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"Invalid {typeof(T).Name} '{value}' for record '{id}'.");

    private static DateTime ParseUtc(string value, string id, string field)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            throw new InvalidOperationException($"Invalid {field} UTC value for record '{id}'.");
        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    private static string FormatUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc) throw new InvalidOperationException("Persistence timestamps must be UTC.");
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }
}