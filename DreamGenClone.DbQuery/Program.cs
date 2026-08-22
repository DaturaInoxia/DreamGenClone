using Microsoft.Data.Sqlite;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var databasePath = FindDatabasePath();
if (!File.Exists(databasePath))
{
    Console.Error.WriteLine($"Development database was not found: {databasePath}");
    return 2;
}

await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
await connection.OpenAsync();

try
{
    return args[0].ToLowerInvariant() switch
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
        "sql" => await PrintSqlFileAsync(connection, RequireArgument(args, 1, "sqlFile"), args.ElementAtOrDefault(2)),
        _ => throw new ArgumentException($"Unknown command '{args[0]}'.")
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
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
    Console.Error.WriteLine("Commands: tables, schema [table], sessions, session <id>, adaptive <id>, themes <id>, evals <id>, transitions <id>, turns <id>, debug <id>, completions <id>, formula <id>, scenario <id>, gate-profiles, gate-rules <themeId>, theme-profiles, rp-themes <profileId>, sql <file> [id]");
}
