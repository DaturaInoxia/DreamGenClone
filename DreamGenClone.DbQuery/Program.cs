using Microsoft.Data.Sqlite;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var commandName = args[0].ToLowerInvariant();
var databasePath = FindDatabasePath();
if (!File.Exists(databasePath))
{
    Console.Error.WriteLine($"Development database was not found: {databasePath}");
    return 2;
}

var connectionMode = commandName is "provider-endpoint-update" or "provider-split-model" ? "ReadWrite" : "ReadOnly";
await using var connection = new SqliteConnection($"Data Source={databasePath};Mode={connectionMode}");
await connection.OpenAsync();

try
{
    return commandName switch
    {
        "tables" => await PrintQueryAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;"),
        "schema" => await PrintSchemaAsync(connection, args.Skip(1).FirstOrDefault()),
        "sessions" => await PrintQueryAsync(connection, "SELECT Id, SessionType, Name, SchemaVersion, UpdatedUtc FROM Sessions ORDER BY UpdatedUtc DESC LIMIT 20;"),
        "session" => await PrintSessionAsync(connection, RequireArgument(args, 1, "sessionId")),
        "adaptive" => await PrintBySessionAsync(connection, "RolePlayV2AdaptiveStates", RequireArgument(args, 1, "sessionId")),
        "themes" => await PrintBySessionAsync(connection, "RolePlayV2ThemeScores", RequireArgument(args, 1, "sessionId"), "Score DESC"),
        "evals" => await PrintBySessionAsync(connection, "RolePlayV2CandidateEvaluations", RequireArgument(args, 1, "sessionId"), "EvaluatedUtc DESC LIMIT 10"),
        "transitions" => await PrintBySessionAsync(connection, "RolePlayV2PhaseTransitions", RequireArgument(args, 1, "sessionId"), "OccurredUtc DESC LIMIT 20"),
        "turns" => await PrintBySessionAsync(connection, "RolePlayV2Turns", RequireArgument(args, 1, "sessionId"), "TurnIndex DESC LIMIT 20"),
        "debug" => await PrintBySessionAsync(connection, "RolePlayDebugEvents", RequireArgument(args, 1, "sessionId"), "CreatedUtc DESC LIMIT 20"),
        "completions" => await PrintBySessionAsync(connection, "RolePlayV2CompletionMetadata", RequireArgument(args, 1, "sessionId"), "CompletedUtc DESC"),
        "formula" => await PrintBySessionAsync(connection, "RolePlayV2FormulaVersionRefs", RequireArgument(args, 1, "sessionId"), "CreatedUtc DESC"),
        "scenario" => await PrintByIdAsync(connection, "Scenarios", RequireArgument(args, 1, "scenarioId")),
        "gate-profiles" => await PrintQueryAsync(connection, "SELECT * FROM NarrativeGateProfiles ORDER BY Name;"),
        "gate-rules" => await PrintByColumnAsync(connection, "RPThemeNarrativeGateRules", "ThemeId", RequireArgument(args, 1, "themeId"), "SortOrder"),
        "theme-profiles" => await PrintQueryAsync(connection, "SELECT * FROM RPThemeProfiles ORDER BY Name;"),
        "rp-themes" => await PrintByColumnAsync(connection, "RPThemeProfileThemeAssignments", "ProfileId", RequireArgument(args, 1, "profileId"), "SortOrder"),
        "provider-endpoint-update" => await UpdateProviderEndpointAsync(
            connection,
            RequireArgument(args, 1, "providerId"),
            RequireArgument(args, 2, "expectedCurrentBaseUrl"),
            RequireArgument(args, 3, "newBaseUrl")),
        "provider-split-model" => await SplitProviderModelAsync(
            connection,
            RequireArgument(args, 1, "sourceProviderId"),
            RequireArgument(args, 2, "modelId"),
            RequireArgument(args, 3, "newProviderName"),
            RequireArgument(args, 4, "newBaseUrl")),
        "sql" => await PrintSqlFileAsync(connection, RequireArgument(args, 1, "sqlFile"), args.ElementAtOrDefault(2)),
        _ => throw new ArgumentException($"Unknown command '{args[0]}'.")
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static async Task<int> UpdateProviderEndpointAsync(
    SqliteConnection connection,
    string providerId,
    string expectedCurrentBaseUrl,
    string newBaseUrl)
{
    if (!Uri.TryCreate(expectedCurrentBaseUrl, UriKind.Absolute, out var expectedUri)
        || expectedUri.Scheme is not ("http" or "https"))
        throw new ArgumentException("expectedCurrentBaseUrl must be an absolute HTTP(S) URL.");
    if (!Uri.TryCreate(newBaseUrl, UriKind.Absolute, out var newUri)
        || newUri.Scheme != "https")
        throw new ArgumentException("newBaseUrl must be an absolute HTTPS URL.");

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    await using var select = connection.CreateCommand();
    select.Transaction = transaction;
    select.CommandText = "SELECT Name, BaseUrl FROM Providers WHERE Id = $providerId;";
    select.Parameters.AddWithValue("$providerId", providerId);

    string providerName;
    string currentBaseUrl;
    await using (var reader = await select.ExecuteReaderAsync())
    {
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"Provider '{providerId}' was not found; no database changes were made.");
        providerName = reader.GetString(0);
        currentBaseUrl = reader.GetString(1);
    }

    if (!string.Equals(currentBaseUrl, expectedCurrentBaseUrl, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Provider '{providerName}' endpoint changed concurrently. Expected '{expectedCurrentBaseUrl}', found '{currentBaseUrl}'; no database changes were made.");
    }

    if (string.Equals(currentBaseUrl, newBaseUrl, StringComparison.Ordinal))
    {
        await transaction.RollbackAsync();
        Console.WriteLine($"Provider endpoint already current: {providerId} | {providerName} | {newBaseUrl}");
        return 0;
    }

    await using var update = connection.CreateCommand();
    update.Transaction = transaction;
    update.CommandText = """
        UPDATE Providers
        SET BaseUrl = $newBaseUrl,
            UpdatedUtc = $updatedUtc
        WHERE Id = $providerId
          AND BaseUrl = $expectedCurrentBaseUrl;
        """;
    update.Parameters.AddWithValue("$newBaseUrl", newBaseUrl);
    update.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("o"));
    update.Parameters.AddWithValue("$providerId", providerId);
    update.Parameters.AddWithValue("$expectedCurrentBaseUrl", expectedCurrentBaseUrl);
    var rowsAffected = await update.ExecuteNonQueryAsync();
    if (rowsAffected != 1)
        throw new InvalidOperationException("Provider endpoint compare-and-swap failed; no database changes were made.");

    await transaction.CommitAsync();
    Console.WriteLine($"Provider endpoint updated: {providerId} | {providerName} | {currentBaseUrl} -> {newBaseUrl}");
    return 0;
}

static async Task<int> SplitProviderModelAsync(
    SqliteConnection connection,
    string sourceProviderId,
    string modelId,
    string newProviderName,
    string newBaseUrl)
{
    if (string.IsNullOrWhiteSpace(newProviderName))
        throw new ArgumentException("newProviderName must not be empty.");
    if (!Uri.TryCreate(newBaseUrl, UriKind.Absolute, out var newUri) || newUri.Scheme != "https")
        throw new ArgumentException("newBaseUrl must be an absolute HTTPS URL.");

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    await using var select = connection.CreateCommand();
    select.Transaction = transaction;
    select.CommandText = """
        SELECT p.Name, rm.DisplayName, rm.ModelIdentifier
        FROM Providers p
        INNER JOIN RegisteredModels rm ON rm.ProviderId = p.Id
        WHERE p.Id = $sourceProviderId
          AND rm.Id = $modelId;
        """;
    select.Parameters.AddWithValue("$sourceProviderId", sourceProviderId);
    select.Parameters.AddWithValue("$modelId", modelId);

    string sourceProviderName;
    string modelDisplayName;
    string modelIdentifier;
    await using (var reader = await select.ExecuteReaderAsync())
    {
        if (!await reader.ReadAsync())
            throw new InvalidOperationException(
                $"Model '{modelId}' was not assigned to provider '{sourceProviderId}'; no database changes were made.");
        sourceProviderName = reader.GetString(0);
        modelDisplayName = reader.GetString(1);
        modelIdentifier = reader.GetString(2);
    }

    var newProviderId = Guid.NewGuid().ToString();
    var now = DateTime.UtcNow.ToString("o");
    await using var insert = connection.CreateCommand();
    insert.Transaction = transaction;
    insert.CommandText = """
        INSERT INTO Providers (
            Id, Name, ProviderType, BaseUrl, ChatCompletionsPath, ImageCapability,
            ImageGenerationPath, ContentPolicy, ImageProtocol, TimeoutSeconds,
            LifecycleStrategyIdentifier, ReadinessPath, ReadinessSuccessContractJson,
            TransitionTimeoutSeconds, TransitionMarginSeconds, ShutdownDrainPolicyJson,
            MaximumActiveRequests, QueueCapacity, CredentialReference, ServerIdentityPolicyJson,
            AllowedNetworkBoundary, ApiKeyEncrypted, IsEnabled, CreatedUtc, UpdatedUtc, Notes)
        SELECT
            $newProviderId, $newProviderName, ProviderType, $newBaseUrl, ChatCompletionsPath,
            ImageCapability, ImageGenerationPath, ContentPolicy, ImageProtocol, TimeoutSeconds,
            LifecycleStrategyIdentifier, ReadinessPath, ReadinessSuccessContractJson,
            TransitionTimeoutSeconds, TransitionMarginSeconds, ShutdownDrainPolicyJson,
            MaximumActiveRequests, QueueCapacity, CredentialReference, ServerIdentityPolicyJson,
            AllowedNetworkBoundary, ApiKeyEncrypted, IsEnabled, $now, $now, Notes
        FROM Providers
        WHERE Id = $sourceProviderId;
        """;
    insert.Parameters.AddWithValue("$newProviderId", newProviderId);
    insert.Parameters.AddWithValue("$newProviderName", newProviderName);
    insert.Parameters.AddWithValue("$newBaseUrl", newBaseUrl);
    insert.Parameters.AddWithValue("$now", now);
    insert.Parameters.AddWithValue("$sourceProviderId", sourceProviderId);
    if (await insert.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException($"Provider '{sourceProviderId}' was not found; no database changes were made.");

    await using var moveModel = connection.CreateCommand();
    moveModel.Transaction = transaction;
    moveModel.CommandText = """
        UPDATE RegisteredModels
        SET ProviderId = $newProviderId
        WHERE Id = $modelId
          AND ProviderId = $sourceProviderId;
        """;
    moveModel.Parameters.AddWithValue("$newProviderId", newProviderId);
    moveModel.Parameters.AddWithValue("$modelId", modelId);
    moveModel.Parameters.AddWithValue("$sourceProviderId", sourceProviderId);
    if (await moveModel.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException("Model provider compare-and-swap failed; no database changes were made.");

    await transaction.CommitAsync();
    Console.WriteLine(
        $"Provider split completed: {newProviderId} | {newProviderName} | {newBaseUrl} | " +
        $"moved {modelDisplayName} ({modelIdentifier}) from {sourceProviderName}");
    return 0;
}

static async Task<int> PrintSchemaAsync(SqliteConnection connection, string? tableName)
{
    if (!string.IsNullOrWhiteSpace(tableName))
        return await PrintQueryAsync(connection, $"PRAGMA table_info({QuoteIdentifier(tableName)});");

    return await PrintQueryAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;");
}

static async Task<int> PrintSessionAsync(SqliteConnection connection, string sessionId)
{
    await PrintByIdAsync(connection, "Sessions", sessionId);
    return await PrintBySessionAsync(connection, "RolePlayV2AdaptiveStates", sessionId);
}

static Task<int> PrintByIdAsync(SqliteConnection connection, string table, string id)
    => PrintByColumnAsync(connection, table, "Id", id);

static Task<int> PrintBySessionAsync(SqliteConnection connection, string table, string sessionId, string? orderBy = null)
    => PrintByColumnAsync(connection, table, "SessionId", sessionId, orderBy);

static async Task<int> PrintByColumnAsync(SqliteConnection connection, string table, string column, string value, string? orderBy = null)
{
    await using var command = connection.CreateCommand();
    command.CommandText = $"SELECT * FROM {QuoteIdentifier(table)} WHERE {QuoteIdentifier(column)} = @value" +
        (string.IsNullOrWhiteSpace(orderBy) ? ";" : $" ORDER BY {orderBy};");
    command.Parameters.AddWithValue("@value", value);
    return await PrintReaderAsync(await command.ExecuteReaderAsync());
}

static async Task<int> PrintSqlFileAsync(SqliteConnection connection, string sqlFile, string? id)
{
    if (!File.Exists(sqlFile))
        throw new FileNotFoundException("SQL file was not found.", sqlFile);

    var sql = await File.ReadAllTextAsync(sqlFile);
    if (!string.IsNullOrWhiteSpace(id))
        sql = sql.Replace("{{id}}", id.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);

    return await PrintQueryAsync(connection, sql);
}

static async Task<int> PrintQueryAsync(SqliteConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return await PrintReaderAsync(await command.ExecuteReaderAsync());
}

static async Task<int> PrintReaderAsync(SqliteDataReader reader)
{
    await using (reader)
    {
        Console.WriteLine(string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)));
        while (await reader.ReadAsync())
        {
            Console.WriteLine(string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? string.Empty : reader.GetValue(index).ToString())));
        }
    }

    return 0;
}

static string RequireArgument(string[] arguments, int index, string name)
    => arguments.ElementAtOrDefault(index) ?? throw new ArgumentException($"Missing required argument '{name}'.");

static string QuoteIdentifier(string identifier)
    => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

static string FindDatabasePath()
{
    for (var current = new DirectoryInfo(Directory.GetCurrentDirectory()); current is not null; current = current.Parent)
    {
        var candidate = Path.Combine(current.FullName, "DreamGenClone.Web", "data", "dreamgenclone.dev.db");
        if (File.Exists(candidate))
            return candidate;
    }

    return Path.Combine(Directory.GetCurrentDirectory(), "DreamGenClone.Web", "data", "dreamgenclone.dev.db");
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage: dotnet run --project DreamGenClone.DbQuery -- <command> [args]");
    Console.Error.WriteLine("Commands: tables, schema [table], sessions, session <id>, adaptive <id>, themes <id>, evals <id>, transitions <id>, turns <id>, debug <id>, completions <id>, formula <id>, scenario <id>, gate-profiles, gate-rules <themeId>, theme-profiles, rp-themes <profileId>, provider-endpoint-update <providerId> <expectedCurrentBaseUrl> <newBaseUrl>, provider-split-model <sourceProviderId> <modelId> <newProviderName> <newBaseUrl>, sql <file> [id]");
}
