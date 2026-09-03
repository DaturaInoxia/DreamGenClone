// Model Manager portable config export/import.
//
// Backing commands wired into the permanent DbQuery dispatcher (Program.cs):
//   modelmanager-export [outFile]      -> write all Providers / RegisteredModels / FunctionModelDefaults
//                                         as a portable JSON file (API keys are NEVER exported)
//   modelmanager-import <jsonFile>     -> transactionally replace the three Model Manager tables on the
//                                         target dev.db with the file contents (full mirror; preserves any
//                                         API keys already entered on the target for matching provider Ids)
//
// The JSON document intentionally stores every enum as its NAME (e.g. "ProviderType": "OpenRouter"),
// never its integer, so the file is human-readable, diff-able, and editable before import. The import
// maps names back to the same integer values.
//
// NOTE: provider API keys are DPAPI-encrypted and machine/user bound, so they cannot cross hosts.
// This transfer deliberately omits them; after importing on another host the operator re-enters keys in
// Model Manager (and defines ModelManagerSecrets entries for any provider that carries a CredentialReference).

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

internal static class ModelManagerTransfer
{
    // Default export location (next to the live dev.db). Git-tracked (see .gitignore exception) so the
    // file can be cloned/pulled to another host. Run from the repo root.
    public const string DefaultExportPath = "DreamGenClone.Web/data/model-manager.export.json";

    public const string FormatMarker = "DreamGenClone.ModelManager.Export";
    public const int FormatVersion = 1;

    // ---------------------------------------------------------------------
    // Column metadata (single source of truth for both export and import).
    // Order = JSON property order + SQL SELECT/INSERT order.
    // ApiKeyEncrypted is deliberately NOT listed: it is never exported and is
    // handled specially on import (target keys are preserved, new rows stay blank).
    // ---------------------------------------------------------------------

    private enum ColumnType { Text, Int, Real, Bool, Enum }

    private sealed record Column(string Name, ColumnType Type, string? EnumOf = null);

    private static readonly IReadOnlyList<Column> ProviderColumns = new[]
    {
        new Column("Id", ColumnType.Text),
        new Column("Name", ColumnType.Text),
        new Column("ProviderType", ColumnType.Enum, "ProviderType"),
        new Column("BaseUrl", ColumnType.Text),
        new Column("ChatCompletionsPath", ColumnType.Text),
        new Column("TimeoutSeconds", ColumnType.Int),
        new Column("IsEnabled", ColumnType.Bool),
        new Column("CreatedUtc", ColumnType.Text),
        new Column("UpdatedUtc", ColumnType.Text),
        new Column("Notes", ColumnType.Text),
        new Column("ImageCapability", ColumnType.Enum, "ImageProviderCapability"),
        new Column("ImageGenerationPath", ColumnType.Text),
        new Column("ContentPolicy", ColumnType.Enum, "ImageContentPolicy"),
        new Column("ImageProtocol", ColumnType.Enum, "ImageProtocol"),
        new Column("LifecycleStrategyIdentifier", ColumnType.Text),
        new Column("ReadinessPath", ColumnType.Text),
        new Column("ReadinessSuccessContractJson", ColumnType.Text),
        new Column("TransitionTimeoutSeconds", ColumnType.Int),
        new Column("TransitionMarginSeconds", ColumnType.Int),
        new Column("ShutdownDrainPolicyJson", ColumnType.Text),
        new Column("MaximumActiveRequests", ColumnType.Int),
        new Column("QueueCapacity", ColumnType.Int),
        new Column("CredentialReference", ColumnType.Text),
        new Column("ServerIdentityPolicyJson", ColumnType.Text),
        new Column("AllowedNetworkBoundary", ColumnType.Text),
    };

    private static readonly IReadOnlyList<Column> ModelColumns = new[]
    {
        new Column("Id", ColumnType.Text),
        new Column("ProviderId", ColumnType.Text),
        new Column("ModelIdentifier", ColumnType.Text),
        new Column("DisplayName", ColumnType.Text),
        new Column("IsEnabled", ColumnType.Bool),
        new Column("CreatedUtc", ColumnType.Text),
        new Column("ContextWindowSize", ColumnType.Int),
        new Column("Quantization", ColumnType.Text),
        new Column("ParameterCount", ColumnType.Text),
        new Column("Notes", ColumnType.Text),
        new Column("SupportsThinkingControl", ColumnType.Bool),
        new Column("ModelKind", ColumnType.Enum, "ModelKind"),
        new Column("ImageSizeSupported", ColumnType.Text),
        new Column("ImageEditorDiffusionModel", ColumnType.Text),
        new Column("ImageEditorTextEncoder", ColumnType.Text),
        new Column("ImageEditorVae", ColumnType.Text),
        new Column("ImageEditorSteps", ColumnType.Int),
        new Column("ImageEditorCfg", ColumnType.Real),
        new Column("ImageEditorSampler", ColumnType.Text),
        new Column("ImageEditorScheduler", ColumnType.Text),
        new Column("ImageEditorDenoise", ColumnType.Real),
        new Column("ImageEditorAuraFlowShift", ColumnType.Real),
        new Column("ImageEditorCfgNormStrength", ColumnType.Real),
        new Column("SupportsImageInput", ColumnType.Bool),
        new Column("MaximumInputImages", ColumnType.Int),
        new Column("MaximumInputImageBytes", ColumnType.Int),
        new Column("MaximumInputImagePixels", ColumnType.Int),
        new Column("MaximumInputImageDimension", ColumnType.Int),
        new Column("AcceptedInputMediaTypes", ColumnType.Text),
        new Column("MaximumResponseBytes", ColumnType.Int),
        new Column("RuntimeRevision", ColumnType.Text),
        new Column("ArtifactRevision", ColumnType.Text),
        new Column("IdentityMechanism", ColumnType.Text),
        new Column("IdentityStrength", ColumnType.Real),
        new Column("IdentityAdapterRef", ColumnType.Text),
        new Column("IdentityClipVisionRef", ColumnType.Text),
        new Column("SceneImageModelFamily", ColumnType.Enum, "SceneImageModelFamily"),
        new Column("PromptDialect", ColumnType.Enum, "SceneImagePromptDialect"),
        new Column("SupportsStructuredJsonSchema", ColumnType.Bool),
        new Column("StructuredOutputMode", ColumnType.Enum, "StructuredOutputMode"),
        new Column("MaximumContextTokens", ColumnType.Int),
        new Column("MaximumOutputTokens", ColumnType.Int),
        new Column("SupportedIdentityStrategiesJson", ColumnType.Text),
    };

    private static readonly IReadOnlyList<Column> FunctionDefaultColumns = new[]
    {
        new Column("Id", ColumnType.Text),
        new Column("FunctionName", ColumnType.Text),
        new Column("ModelId", ColumnType.Text),
        new Column("Temperature", ColumnType.Real),
        new Column("TopP", ColumnType.Real),
        new Column("MaxTokens", ColumnType.Int),
        new Column("UpdatedUtc", ColumnType.Text),
        new Column("MaxConcurrentJobs", ColumnType.Int),
        new Column("ThinkingMode", ColumnType.Enum, "ThinkingMode"),
        new Column("DurableJobLeaseSeconds", ColumnType.Int),
        new Column("DurableJobPollIntervalMilliseconds", ColumnType.Int),
        new Column("TransientRetryCount", ColumnType.Int),
        new Column("TransientRetryDelaysSecondsJson", ColumnType.Text),
        new Column("DiagnosticsRetentionDays", ColumnType.Int),
        new Column("MaximumCatalogueEntries", ColumnType.Int),
    };

    // Enum values are sequential from 0, so the array index IS the stored integer.
    private static readonly IReadOnlyDictionary<string, string[]> EnumNames = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["ProviderType"] = new[] { "LmStudio", "TogetherAI", "OpenRouter" },
        ["ImageProviderCapability"] = new[] { "None", "TextAndImage", "ImageOnly" },
        ["ImageContentPolicy"] = new[] { "Unknown", "SfwFiltered", "AdultAllowed", "AdultAllowedConfigurable" },
        ["ImageProtocol"] = new[] { "OpenAiImages", "ComfyUi", "ComfyUiServerless" },
        ["ModelKind"] = new[] { "Text", "Image" },
        ["SceneImageModelFamily"] = new[] { "Unknown", "Pony", "Sdxl", "Api" },
        ["SceneImagePromptDialect"] = new[] { "Unknown", "PonyV6Tags", "SdxlNaturalLanguage", "NaturalLanguage" },
        ["StructuredOutputMode"] = new[] { "None", "StrictJsonSchema", "JsonObject" },
        ["ThinkingMode"] = new[] { "Default", "Enabled", "Disabled" },
    };

    // ---------------------------------------------------------------------
    // Export
    // ---------------------------------------------------------------------

    public static async Task<int> ExportAsync(SqliteConnection connection, string outFile)
    {
        var providers = await ReadRowsAsync(connection, "Providers", ProviderColumns);
        var models = await ReadRowsAsync(connection, "RegisteredModels", ModelColumns);
        var functionDefaults = await ReadRowsAsync(connection, "FunctionModelDefaults", FunctionDefaultColumns);

        var document = new JsonObject
        {
            ["format"] = FormatMarker,
            ["formatVersion"] = FormatVersion,
            ["generatedUtc"] = DateTime.UtcNow.ToString("o"),
            ["apiKeysOmitted"] = true,
            ["note"] = "Provider API keys and credential secrets are intentionally omitted. After importing on the target host, re-enter each provider's API key in Model Manager; for any provider with a CredentialReference, add the matching plaintext secret to the git-ignored appsettings.Local.json 'ModelManagerSecrets' section.",
            ["providers"] = providers,
            ["registeredModels"] = models,
            ["functionDefaults"] = functionDefaults,
        };

        var fullPath = Path.GetFullPath(outFile);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(fullPath, json);

        Console.WriteLine(
            $"Exported {providers.Count} providers, {models.Count} registered models, " +
            $"{functionDefaults.Count} function defaults -> {fullPath}");
        Console.WriteLine("API keys were NOT exported. Re-enter provider keys on the target host after import.");
        return 0;
    }

    private static async Task<JsonArray> ReadRowsAsync(
        SqliteConnection connection,
        string table,
        IReadOnlyList<Column> columns)
    {
        var selectList = string.Join(", ", columns.Select(column => Quote(column.Name)));
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {selectList} FROM {Quote(table)} ORDER BY {Quote(columns[0].Name)};";
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new JsonArray();
        while (await reader.ReadAsync())
        {
            var row = new JsonObject();
            for (var index = 0; index < columns.Count; index++)
            {
                row[columns[index].Name] = reader.IsDBNull(index)
                    ? null
                    : ReadJsonValue(reader, index, columns[index]);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static JsonNode? ReadJsonValue(SqliteDataReader reader, int index, Column column)
        => column.Type switch
        {
            ColumnType.Text => JsonValue.Create(reader.GetString(index)),
            ColumnType.Int => JsonValue.Create(reader.GetInt64(index)),
            ColumnType.Real => JsonValue.Create(reader.GetDouble(index)),
            ColumnType.Bool => JsonValue.Create(reader.GetInt64(index) != 0),
            ColumnType.Enum => JsonValue.Create(NameOfEnum(column.EnumOf!, reader.GetInt32(index))),
            _ => throw new InvalidOperationException($"Unhandled column type '{column.Type}' for '{column.Name}'."),
        };

    private static string NameOfEnum(string enumName, int value)
    {
        var names = EnumNames[enumName];
        if (value < 0 || value >= names.Length)
        {
            throw new InvalidOperationException(
                $"Database holds unknown {enumName} value {value}; this exporter cannot represent it. " +
                $"Supported values: {string.Join(", ", names)}.");
        }

        return names[value];
    }

    // ---------------------------------------------------------------------
    // Import
    // ---------------------------------------------------------------------

    public static async Task<int> ImportAsync(SqliteConnection connection, string jsonFile)
    {
        if (!File.Exists(jsonFile))
        {
            throw new FileNotFoundException($"Model Manager import file not found: {jsonFile}");
        }

        JsonObject document;
        try
        {
            document = JsonNode.Parse(await File.ReadAllTextAsync(jsonFile)) as JsonObject
                       ?? throw new JsonException("root must be an object");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"Import file is not valid JSON: {exception.Message}");
        }

        if (document["format"]?.GetValue<string>() != FormatMarker)
        {
            throw new ArgumentException(
                $"'{jsonFile}' is not a DreamGenClone model-manager export (missing or wrong 'format' marker).");
        }

        var version = document["formatVersion"]?.GetValue<int>() ?? 0;
        if (version > FormatVersion)
        {
            throw new ArgumentException(
                $"Export formatVersion {version} is newer than this importer supports (max {FormatVersion}).");
        }

        var providers = RequireArray(document, "providers");
        var models = RequireArray(document, "registeredModels");
        var functionDefaults = RequireArray(document, "functionDefaults");

        // ---- Validate the whole file BEFORE touching the database (fail-fast, no partial writes) ----
        var problems = new List<string>();
        var providerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in providers)
        {
            ValidateRow(node, "providers", ProviderColumns, problems);
            if (node is JsonObject row)
            {
                providerIds.Add(GetRequiredString(row, "Id", "providers", problems));
            }
        }

        var modelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in models)
        {
            ValidateRow(node, "registeredModels", ModelColumns, problems);
            if (node is JsonObject row)
            {
                var id = GetRequiredString(row, "Id", "registeredModels", problems);
                modelIds.Add(id);
                var providerId = GetRequiredString(row, "ProviderId", "registeredModels", problems);
                if (!providerIds.Contains(providerId))
                {
                    problems.Add($"registeredModels row '{id}' references ProviderId '{providerId}' that is not present in 'providers'.");
                }
            }
        }

        foreach (var node in functionDefaults)
        {
            ValidateRow(node, "functionDefaults", FunctionDefaultColumns, problems);
            if (node is JsonObject row)
            {
                var id = GetRequiredString(row, "Id", "functionDefaults", problems);
                var modelId = GetRequiredString(row, "ModelId", "functionDefaults", problems);
                if (!modelIds.Contains(modelId))
                {
                    problems.Add($"functionDefaults row '{id}' references ModelId '{modelId}' that is not present in 'registeredModels'.");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                $"Model Manager import file failed validation ({problems.Count} problem(s)); no changes were made.\n  "
                + string.Join("\n  ", problems));
        }

        // ---- Preserve API keys already entered on the target (matching provider Id only) ----
        var existingKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var keyCommand = connection.CreateCommand())
        {
            keyCommand.CommandText = "SELECT Id, ApiKeyEncrypted FROM Providers WHERE ApiKeyEncrypted IS NOT NULL AND ApiKeyEncrypted <> '';";
            await using var keyReader = await keyCommand.ExecuteReaderAsync();
            while (await keyReader.ReadAsync())
            {
                existingKeys[keyReader.GetString(0)] = keyReader.GetString(1);
            }
        }

        // Foreign-key enforcement ON so the replace mirrors the app's own constraints.
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync();
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        try
        {
            // Children first, then parents (FK-safe regardless of cascade configuration).
            await DeleteAllAsync(connection, transaction, "FunctionModelDefaults");
            await DeleteAllAsync(connection, transaction, "RegisteredModels");
            await DeleteAllAsync(connection, transaction, "Providers");

            var preservedKeys = 0;
            foreach (var node in providers)
            {
                var row = (JsonObject)node!;
                existingKeys.TryGetValue(row["Id"]!.GetValue<string>(), out var key);
                if (!string.IsNullOrEmpty(key))
                {
                    preservedKeys++;
                }

                await InsertProviderAsync(connection, transaction, row, key);
            }

            foreach (var node in models)
            {
                await InsertRowAsync(connection, transaction, "RegisteredModels", ModelColumns, (JsonObject)node!);
            }

            foreach (var node in functionDefaults)
            {
                await InsertRowAsync(connection, transaction, "FunctionModelDefaults", FunctionDefaultColumns, (JsonObject)node!);
            }

            await transaction.CommitAsync();

            Console.WriteLine(
                $"Imported {providers.Count} providers, {models.Count} registered models, " +
                $"{functionDefaults.Count} function defaults -> replaced the Model Manager tables on the target database.");
            Console.WriteLine(
                preservedKeys == 0
                    ? "No existing provider API keys were preserved. Re-enter each provider's API key in Model Manager."
                    : $"Preserved {preservedKeys} existing provider API key(s) already entered on this host (matched by provider Id). Re-enter keys for any newly added providers.");
            return 0;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static JsonArray RequireArray(JsonObject document, string property)
    {
        if (document[property] is not JsonArray array)
        {
            throw new ArgumentException($"Import file is missing the required '{property}' array.");
        }

        return array;
    }

    private static void ValidateRow(JsonNode? node, string section, IReadOnlyList<Column> columns, List<string> problems)
    {
        if (node is not JsonObject row)
        {
            problems.Add($"{section} contains a non-object entry.");
            return;
        }

        foreach (var column in columns)
        {
            if (column.Type == ColumnType.Enum && row.TryGetPropertyValue(column.Name, out var enumNode)
                && enumNode is not null && enumNode.GetValueKind() != JsonValueKind.Null)
            {
                var name = enumNode.GetValue<string>();
                if (!EnumNames[column.EnumOf!].Contains(name, StringComparer.Ordinal))
                {
                    problems.Add(
                        $"{section} '{row["Id"]?.GetValue<string>()}' has invalid {column.Name} '{name}'. " +
                        $"Supported {column.EnumOf} values: {string.Join(", ", EnumNames[column.EnumOf!])}.");
                }
            }
        }
    }

    private static string GetRequiredString(JsonObject row, string property, string section, List<string> problems)
    {
        if (row[property] is { } node && node.GetValueKind() == JsonValueKind.String)
        {
            var value = node.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        problems.Add($"{section} row is missing required '{property}'.");
        return string.Empty;
    }

    private static async Task DeleteAllAsync(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {Quote(table)};";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertProviderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JsonObject row,
        string? preservedApiKey)
    {
        var names = ProviderColumns.Select(column => column.Name)
            .Append("ApiKeyEncrypted")
            .ToArray();
        var insertSql =
            $"INSERT INTO {Quote("Providers")} ({string.Join(", ", names.Select(Quote))}) " +
            $"VALUES ({string.Join(", ", names.Select(name => "$" + name))});";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = insertSql;

        foreach (var column in ProviderColumns)
        {
            command.Parameters.Add("$" + column.Name, SqliteTypeFor(column.Type));
        }

        command.Parameters.Add("$ApiKeyEncrypted", SqliteType.Text);

        foreach (var column in ProviderColumns)
        {
            command.Parameters["$" + column.Name].Value = ToParameterValue(row, column);
        }

        command.Parameters["$ApiKeyEncrypted"].Value = string.IsNullOrEmpty(preservedApiKey)
            ? DBNull.Value
            : preservedApiKey;

        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        IReadOnlyList<Column> columns,
        JsonObject row)
    {
        var insertSql =
            $"INSERT INTO {Quote(table)} ({string.Join(", ", columns.Select(column => Quote(column.Name)))}) " +
            $"VALUES ({string.Join(", ", columns.Select(column => "$" + column.Name))});";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = insertSql;

        foreach (var column in columns)
        {
            command.Parameters.Add("$" + column.Name, SqliteTypeFor(column.Type));
        }

        foreach (var column in columns)
        {
            command.Parameters["$" + column.Name].Value = ToParameterValue(row, column);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static SqliteType SqliteTypeFor(ColumnType type)
        => type switch
        {
            ColumnType.Int or ColumnType.Bool or ColumnType.Enum => SqliteType.Integer,
            ColumnType.Real => SqliteType.Real,
            _ => SqliteType.Text,
        };

    private static object ToParameterValue(JsonObject row, Column column)
    {
        if (!row.TryGetPropertyValue(column.Name, out var node) || node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return DBNull.Value;
        }

        return column.Type switch
        {
            ColumnType.Text => node.GetValue<string>(),
            ColumnType.Int => node.GetValue<long>(),
            ColumnType.Real => node.GetValue<double>(),
            ColumnType.Bool => node.GetValue<bool>() ? 1L : 0L,
            ColumnType.Enum => (long)ValueOfEnum(column.EnumOf!, node.GetValue<string>()),
            _ => throw new InvalidOperationException($"Unhandled column type '{column.Type}' for '{column.Name}'."),
        };
    }

    private static int ValueOfEnum(string enumName, string name)
    {
        var names = EnumNames[enumName];
        for (var index = 0; index < names.Length; index++)
        {
            if (string.Equals(names[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        throw new ArgumentException(
            $"Unknown {enumName} '{name}'. Supported values: {string.Join(", ", names)}.");
    }

    private static string Quote(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
