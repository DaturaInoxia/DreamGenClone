using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class SceneIdentityEvaluationRepository : ISceneIdentityEvaluationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ForbiddenEvidenceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "apiKey", "api_key", "authorization", "password", "secret", "token"
    };

    private readonly string _connectionString;

    public SceneIdentityEvaluationRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task CreateCasesAsync(
        IReadOnlyList<SceneIdentityEvaluationCase> cases,
        CancellationToken cancellationToken = default)
    {
        if (cases.Count == 0) throw new InvalidOperationException("An identity evaluation run requires at least one case.");
        foreach (var evaluationCase in cases) ValidateCase(evaluationCase);
        if (cases.Select(value => value.EvaluationRunId).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidOperationException("Identity evaluation cases must belong to one explicit run.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var evaluationCase in cases)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO SceneIdentityEvaluationCases
                    (Id, EvaluationRunId, CapabilityCellId, Ordinal, PayloadJson, CreatedUtc)
                VALUES ($id, $run, $cell, $ordinal, $payload, $created);
                """;
            command.Parameters.AddWithValue("$id", evaluationCase.Id.Trim());
            command.Parameters.AddWithValue("$run", evaluationCase.EvaluationRunId.Trim());
            command.Parameters.AddWithValue("$cell", evaluationCase.CapabilityCellId.Trim());
            command.Parameters.AddWithValue("$ordinal", evaluationCase.Ordinal);
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(evaluationCase, JsonOptions));
            command.Parameters.AddWithValue("$created", evaluationCase.CreatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SceneIdentityEvaluationCase?> GetCaseAsync(
        string caseId,
        CancellationToken cancellationToken = default)
    {
        Require(caseId, "Evaluation case id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM SceneIdentityEvaluationCases WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", caseId.Trim());
        return Deserialize<SceneIdentityEvaluationCase>(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<SceneIdentityEvaluationCase>> ListCasesAsync(
        string evaluationRunId,
        CancellationToken cancellationToken = default)
    {
        Require(evaluationRunId, "Evaluation run id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM SceneIdentityEvaluationCases WHERE EvaluationRunId = $run ORDER BY Ordinal;";
        command.Parameters.AddWithValue("$run", evaluationRunId.Trim());
        return await ReadManyAsync<SceneIdentityEvaluationCase>(command, cancellationToken);
    }

    public async Task AddResultAsync(
        SceneIdentityEvaluationResult result,
        CancellationToken cancellationToken = default)
    {
        ValidateResult(result);
        await using var connection = await OpenAsync(cancellationToken);
        await RequireCaseAsync(connection, result.EvaluationCaseId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SceneIdentityEvaluationResults
                (Id, EvaluationCaseId, AttemptId, OutputSha256, PayloadJson, ReviewedUtc)
            VALUES ($id, $case, $attempt, $sha, $payload, $reviewed);
            """;
        command.Parameters.AddWithValue("$id", result.Id.Trim());
        command.Parameters.AddWithValue("$case", result.EvaluationCaseId.Trim());
        command.Parameters.AddWithValue("$attempt", result.AttemptId.Trim());
        command.Parameters.AddWithValue("$sha", result.OutputSha256.Trim());
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("$reviewed", result.ReviewedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SceneIdentityEvaluationResult>> ListResultsAsync(
        string evaluationRunId,
        CancellationToken cancellationToken = default)
    {
        Require(evaluationRunId, "Evaluation run id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT result.PayloadJson
            FROM SceneIdentityEvaluationResults result
            INNER JOIN SceneIdentityEvaluationCases evaluationCase ON evaluationCase.Id = result.EvaluationCaseId
            WHERE evaluationCase.EvaluationRunId = $run
            ORDER BY evaluationCase.Ordinal, result.ReviewedUtc;
            """;
        command.Parameters.AddWithValue("$run", evaluationRunId.Trim());
        return await ReadManyAsync<SceneIdentityEvaluationResult>(command, cancellationToken);
    }

    public async Task RecordDecisionAsync(
        CharacterIdentityDecision decision,
        CancellationToken cancellationToken = default)
    {
        ValidateDecision(decision);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterIdentityDecisions
                (Id, IdentityPackId, EvaluationRunId, Decision, PayloadJson, CreatedUtc)
            VALUES ($id, $pack, $run, $decision, $payload, $created);
            """;
        command.Parameters.AddWithValue("$id", decision.Id.Trim());
        command.Parameters.AddWithValue("$pack", decision.IdentityPackId.Trim());
        command.Parameters.AddWithValue("$run", decision.EvaluationRunId.Trim());
        command.Parameters.AddWithValue("$decision", decision.Decision.ToString());
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(decision, JsonOptions));
        command.Parameters.AddWithValue("$created", decision.CreatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterIdentityDecision>> ListDecisionsAsync(
        string identityPackId,
        CancellationToken cancellationToken = default)
    {
        Require(identityPackId, "Identity pack id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterIdentityDecisions WHERE IdentityPackId = $pack ORDER BY CreatedUtc;";
        command.Parameters.AddWithValue("$pack", identityPackId.Trim());
        return await ReadManyAsync<CharacterIdentityDecision>(command, cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
        await foreignKeys.ExecuteNonQueryAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SceneIdentityEvaluationCases (
                Id TEXT PRIMARY KEY,
                EvaluationRunId TEXT NOT NULL,
                CapabilityCellId TEXT NOT NULL,
                Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
                PayloadJson TEXT NOT NULL CHECK (json_valid(PayloadJson)),
                CreatedUtc TEXT NOT NULL,
                UNIQUE (EvaluationRunId, Ordinal)
            );
            CREATE INDEX IF NOT EXISTS IX_SceneIdentityEvaluationCases_Run
                ON SceneIdentityEvaluationCases (EvaluationRunId, Ordinal);

            CREATE TABLE IF NOT EXISTS SceneIdentityEvaluationResults (
                Id TEXT PRIMARY KEY,
                EvaluationCaseId TEXT NOT NULL,
                AttemptId TEXT NOT NULL,
                OutputSha256 TEXT NOT NULL,
                PayloadJson TEXT NOT NULL CHECK (json_valid(PayloadJson)),
                ReviewedUtc TEXT NOT NULL,
                FOREIGN KEY (EvaluationCaseId) REFERENCES SceneIdentityEvaluationCases(Id) ON DELETE RESTRICT,
                UNIQUE (EvaluationCaseId, AttemptId, OutputSha256, ReviewedUtc)
            );
            CREATE INDEX IF NOT EXISTS IX_SceneIdentityEvaluationResults_Case
                ON SceneIdentityEvaluationResults (EvaluationCaseId, ReviewedUtc);

            CREATE TABLE IF NOT EXISTS CharacterIdentityDecisions (
                Id TEXT PRIMARY KEY,
                IdentityPackId TEXT NOT NULL,
                EvaluationRunId TEXT NOT NULL,
                Decision TEXT NOT NULL,
                PayloadJson TEXT NOT NULL CHECK (json_valid(PayloadJson)),
                CreatedUtc TEXT NOT NULL,
                UNIQUE (IdentityPackId, EvaluationRunId)
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterIdentityDecisions_Pack
                ON CharacterIdentityDecisions (IdentityPackId, CreatedUtc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateCase(SceneIdentityEvaluationCase value)
    {
        Require(value.Id, "Evaluation case id");
        Require(value.EvaluationRunId, "Evaluation run id");
        Require(value.CapabilityCellId, "Capability cell id");
        if (value.Ordinal < 0) throw new InvalidOperationException("Evaluation case ordinal cannot be negative.");
        ValidJson(value.CharacterPairJson, "Character pair", JsonValueKind.Array);
        Require(value.PoseKey, "Evaluation pose key");
        Require(value.ViewKey, "Evaluation view key");
        RequireSha256(value.PromptHash, "Prompt hash");
        RequireSha256(value.ControlHash, "Control hash");
        ValidJson(value.ExpectedConstraintsJson, "Expected constraints", JsonValueKind.Object);
        RequireUtc(value.CreatedUtc, "Evaluation case creation time");
    }

    private static void ValidateResult(SceneIdentityEvaluationResult value)
    {
        Require(value.Id, "Evaluation result id");
        Require(value.EvaluationCaseId, "Evaluation case id");
        Require(value.AttemptId, "Evaluation attempt id");
        RequireSha256(value.OutputSha256, "Evaluation output hash");
        using var scores = ValidJson(value.ConstraintScoresJson, "Constraint scores", JsonValueKind.Object);
        if (!scores.RootElement.EnumerateObject().Any())
            throw new InvalidOperationException("Constraint scores require at least one scored dimension.");
        foreach (var score in scores.RootElement.EnumerateObject())
        {
            if (score.Value.ValueKind != JsonValueKind.String
                || !Enum.TryParse<SceneIdentityConstraintScore>(score.Value.GetString(), false, out _))
                throw new InvalidOperationException($"Constraint score '{score.Name}' must be Pass, Fail, or NotScored.");
        }
        Require(value.Reviewer, "Evaluation reviewer");
        RequireUtc(value.ReviewedUtc, "Evaluation review time");
    }

    private static void ValidateDecision(CharacterIdentityDecision value)
    {
        Require(value.Id, "Identity decision id");
        Require(value.IdentityPackId, "Identity decision pack id");
        Require(value.EvaluationRunId, "Identity decision run id");
        if (!Enum.IsDefined(value.Decision)) throw new InvalidOperationException("Identity decision value is required.");
        using var evidence = ValidJson(value.EvidenceJson, "Identity decision evidence", JsonValueKind.Object);
        RejectSecrets(evidence.RootElement, "Identity decision evidence");
        Require(value.Rationale, "Identity decision rationale");
        RequireUtc(value.CreatedUtc, "Identity decision creation time");
    }

    private static JsonDocument ValidJson(string value, string label, JsonValueKind expectedKind)
    {
        Require(value, label);
        try
        {
            var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != expectedKind)
            {
                document.Dispose();
                throw new InvalidOperationException($"{label} must be a JSON {expectedKind.ToString().ToLowerInvariant()}.");
            }
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{label} must be valid JSON.", exception);
        }
    }

    private static void RejectSecrets(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (ForbiddenEvidenceKeys.Contains(property.Name))
                    throw new InvalidOperationException($"{path} contains forbidden secret field '{property.Name}'.");
                RejectSecrets(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray()) RejectSecrets(item, $"{path}[{index++}]");
        }
    }

    private static async Task RequireCaseAsync(
        SqliteConnection connection,
        string caseId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SceneIdentityEvaluationCases WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", caseId.Trim());
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException($"Identity evaluation case '{caseId}' was not found.");
    }

    private static async Task<IReadOnlyList<T>> ReadManyAsync<T>(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions)
                ?? throw new InvalidOperationException($"Persisted {typeof(T).Name} payload was null."));
        }
        return results;
    }

    private static T? Deserialize<T>(object? value) where T : class =>
        value is string json
            ? JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Persisted {typeof(T).Name} payload was null.")
            : null;

    private static void RequireSha256(string value, string label)
    {
        Require(value, label);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"{label} must be a 64-character SHA-256 value.");
    }

    private static void RequireUtc(DateTime value, string label)
    {
        if (value.Kind != DateTimeKind.Utc) throw new InvalidOperationException($"{label} must be UTC.");
    }

    private static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }
}