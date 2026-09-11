using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

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

var connectionMode = commandName is "provider-endpoint-update" or "provider-split-model" or "provider-timeout-update" or "provider-api-key-update" or "b100-analyzer-configure" or "b100-analyzer-openrouter-configure" or "biglust-image-configure" or "qwen-edit-serverless-configure" or "qwen-edit-local-aio-configure" or "api-image-configure" or "api-image-catalog" or "turn-membership-reconcile" or "b100-settle-plan" or "scene-asset-retag" or "set-identity-strength" or "character-figure-update" or "local-comfyui-configure" or "modelmanager-import" or "sql" ? "ReadWrite" : "ReadOnly";
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
        "provider-timeout-update" => await UpdateProviderTimeoutAsync(
            connection,
            RequireArgument(args, 1, "providerId"),
            RequireArgument(args, 2, "expectedCurrentTimeoutSeconds"),
            RequireArgument(args, 3, "newTimeoutSeconds")),
        "provider-api-key-update" => await UpdateProviderApiKeyAsync(
            connection,
            RequireArgument(args, 1, "providerName"),
            RequireArgument(args, 2, "environmentVariableName")),
        "b100-analyzer-configure" => await ConfigureB100AnalyzerAsync(connection),
        "b100-analyzer-openrouter-configure" => await ConfigureB100OpenRouterAnalyzerAsync(connection),
        "biglust-image-configure" => await ConfigureBigLustImageAsync(connection),
        "qwen-edit-serverless-configure" => await ConfigureQwenEditServerlessAsync(connection),
        "qwen-edit-local-aio-configure" => await ConfigureQwenEditLocalAioAsync(connection),
        "local-comfyui-configure" => await ConfigureLocalComfyUiAsync(
            connection,
            RequireArgument(args, 1, "baseUrl")),
        "set-identity-strength" => await SetIdentityStrengthAsync(
            connection,
            RequireArgument(args, 1, "modelIdentifier"),
            RequireArgument(args, 2, "strength")),
        "api-image-configure" => await ConfigureApiImageModelsAsync(connection),
        "api-image-catalog" => await ConfigureApiImageCatalogAsync(connection),
        "turn-membership-reconcile" => await ReconcileTurnMembershipsAsync(connection, RequireArgument(args, 1, "sessionId")),
        "b100-settle-plan" => await SettleStaleProductionPlanAsync(connection, RequireArgument(args, 1, "planId")),
        "scene-asset-retag" => await RetagSceneAssetAsync(
            connection,
            RequireArgument(args, 1, "assetId"),
            RequireArgument(args, 2, "expectedCurrentType"),
            RequireArgument(args, 3, "newType")),
        "character-figure-update" => await UpdateCharacterFigureAsync(
            connection,
            RequireArgument(args, 1, "scenarioId"),
            RequireArgument(args, 2, "characterName"),
            RequireArgument(args, 3, "weight"),
            RequireArgument(args, 4, "bustSize"),
            RequireArgument(args, 5, "buttSize")),
        "modelmanager-export" => await ModelManagerTransfer.ExportAsync(
            connection,
            args.ElementAtOrDefault(1) ?? ModelManagerTransfer.DefaultExportPath),
        "modelmanager-import" => await ModelManagerTransfer.ImportAsync(
            connection,
            RequireArgument(args, 1, "jsonFile")),
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

static async Task<int> UpdateProviderTimeoutAsync(
    SqliteConnection connection,
    string providerId,
    string expectedCurrentTimeoutSeconds,
    string newTimeoutSeconds)
{
    if (!int.TryParse(expectedCurrentTimeoutSeconds, out var expectedTimeout) || expectedTimeout <= 0)
        throw new ArgumentException("expectedCurrentTimeoutSeconds must be a positive integer.");
    if (!int.TryParse(newTimeoutSeconds, out var newTimeout) || newTimeout <= 0)
        throw new ArgumentException("newTimeoutSeconds must be a positive integer.");

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    await using var select = connection.CreateCommand();
    select.Transaction = transaction;
    select.CommandText = "SELECT Name, TimeoutSeconds FROM Providers WHERE Id = $providerId;";
    select.Parameters.AddWithValue("$providerId", providerId);

    string providerName;
    int currentTimeout;
    await using (var reader = await select.ExecuteReaderAsync())
    {
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"Provider '{providerId}' was not found; no database changes were made.");
        providerName = reader.GetString(0);
        currentTimeout = reader.GetInt32(1);
    }

    if (currentTimeout != expectedTimeout)
    {
        throw new InvalidOperationException(
            $"Provider '{providerName}' timeout changed concurrently. Expected {expectedTimeout}, found {currentTimeout}; no database changes were made.");
    }

    if (currentTimeout == newTimeout)
    {
        await transaction.RollbackAsync();
        Console.WriteLine($"Provider timeout already current: {providerId} | {providerName} | {newTimeout}s");
        return 0;
    }

    await using var update = connection.CreateCommand();
    update.Transaction = transaction;
    update.CommandText = """
        UPDATE Providers
        SET TimeoutSeconds = $newTimeoutSeconds,
            UpdatedUtc = $updatedUtc
        WHERE Id = $providerId
          AND TimeoutSeconds = $expectedCurrentTimeoutSeconds;
        """;
    update.Parameters.AddWithValue("$newTimeoutSeconds", newTimeout);
    update.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("o"));
    update.Parameters.AddWithValue("$providerId", providerId);
    update.Parameters.AddWithValue("$expectedCurrentTimeoutSeconds", expectedTimeout);
    var rowsAffected = await update.ExecuteNonQueryAsync();
    if (rowsAffected != 1)
        throw new InvalidOperationException("Provider timeout compare-and-swap failed; no database changes were made.");

    await transaction.CommitAsync();
    Console.WriteLine($"Provider timeout updated: {providerId} | {providerName} | {currentTimeout}s -> {newTimeout}s");
    return 0;
}

static async Task<int> RetagSceneAssetAsync(
    SqliteConnection connection,
    string assetId,
    string expectedCurrentType,
    string newType)
{
    var normalizedNewType = newType.Trim();
    var allowedTypes = new[] { "Location", "Wardrobe", "Prop", "Style", "CharacterFace", "CharacterBody" };
    var canonicalType = allowedTypes.FirstOrDefault(
        t => string.Equals(t, normalizedNewType, StringComparison.OrdinalIgnoreCase));
    if (canonicalType is null)
    {
        throw new ArgumentException(
            $"newType must be one of: {string.Join(", ", allowedTypes)}.");
    }

    var normalizedExpected = expectedCurrentType.Trim();
    if (string.Equals(normalizedExpected, canonicalType, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"Scene asset type already current: {assetId} | {canonicalType}");
        return 0;
    }

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    await using var select = connection.CreateCommand();
    select.Transaction = transaction;
    select.CommandText = "SELECT Name, COALESCE(Type, '') FROM SceneAssets WHERE Id = $assetId;";
    select.Parameters.AddWithValue("$assetId", assetId.Trim());

    string assetName;
    string currentType;
    await using (var reader = await select.ExecuteReaderAsync())
    {
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"Scene asset '{assetId}' was not found; no database changes were made.");
        assetName = reader.GetString(0);
        currentType = reader.GetString(1);
    }

    if (!string.Equals(currentType, normalizedExpected, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Scene asset '{assetName}' type changed concurrently. Expected '{expectedCurrentType}', found '{currentType}'; no database changes were made.");
    }

    await using var update = connection.CreateCommand();
    update.Transaction = transaction;
    update.CommandText = """
        UPDATE SceneAssets
        SET Type = $newType,
            UpdatedUtc = $updatedUtc
        WHERE Id = $assetId
          AND COALESCE(Type, '') = $expectedCurrentType;
        """;
    update.Parameters.AddWithValue("$newType", canonicalType);
    update.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("o"));
    update.Parameters.AddWithValue("$assetId", assetId.Trim());
    update.Parameters.AddWithValue("$expectedCurrentType", normalizedExpected);
    if (await update.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException("Scene asset type compare-and-swap failed; no database changes were made.");

    await transaction.CommitAsync();
    Console.WriteLine($"Scene asset type updated: {assetId} | {assetName} | {currentType} -> {canonicalType}");
    return 0;
}

static async Task<int> UpdateProviderApiKeyAsync(
    SqliteConnection connection,
    string providerName,
    string environmentVariableName)
{
    if (string.IsNullOrWhiteSpace(environmentVariableName))
        throw new ArgumentException("environmentVariableName must not be empty.");

    var plainTextApiKey = Environment.GetEnvironmentVariable(environmentVariableName);
    if (string.IsNullOrEmpty(plainTextApiKey))
        throw new InvalidOperationException(
            $"Environment variable '{environmentVariableName}' is missing or empty; no database changes were made.");
    if (!OperatingSystem.IsWindows())
        throw new PlatformNotSupportedException("API key encryption is only supported on Windows.");

    var encryptedApiKey = Convert.ToBase64String(ProtectedData.Protect(
        System.Text.Encoding.UTF8.GetBytes(plainTextApiKey),
        optionalEntropy: null,
        DataProtectionScope.CurrentUser));

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    await using var select = connection.CreateCommand();
    select.Transaction = transaction;
    select.CommandText = "SELECT Id FROM Providers WHERE Name = $providerName;";
    select.Parameters.AddWithValue("$providerName", providerName);

    var providerIds = new List<string>();
    await using (var reader = await select.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            providerIds.Add(reader.GetString(0));
    }

    if (providerIds.Count != 1)
        throw new InvalidOperationException(
            $"Expected exactly one provider named '{providerName}', found {providerIds.Count}; no database changes were made.");

    await using var update = connection.CreateCommand();
    update.Transaction = transaction;
    update.CommandText = """
        UPDATE Providers
        SET ApiKeyEncrypted = $apiKeyEncrypted,
            UpdatedUtc = $updatedUtc
                WHERE Id = $providerId
                    AND Name = $providerName;
        """;
    update.Parameters.AddWithValue("$apiKeyEncrypted", encryptedApiKey);
    update.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("o"));
    update.Parameters.AddWithValue("$providerId", providerIds[0]);
    update.Parameters.AddWithValue("$providerName", providerName);

    if (await update.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException($"Provider '{providerName}' changed concurrently; no database changes were made.");

    await transaction.CommitAsync();
    Console.WriteLine($"Provider API key updated from environment variable '{environmentVariableName}': {providerName}");
    return 0;
}

static async Task<int> ConfigureB100AnalyzerAsync(SqliteConnection connection)
{
    const string functionName = "RolePlaySceneBeatAnalyzer";
    const string providerName = "DeepSeek";
    const string modelIdentifier = "deepseek-v4-flash";

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    await using var columnCheck = connection.CreateCommand();
    columnCheck.Transaction = transaction;
    columnCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RegisteredModels') WHERE name = 'StructuredOutputMode';";
    if (Convert.ToInt64(await columnCheck.ExecuteScalarAsync()) == 0)
    {
        await using var alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = "ALTER TABLE RegisteredModels ADD COLUMN StructuredOutputMode INTEGER NOT NULL DEFAULT 0;";
        await alter.ExecuteNonQueryAsync();

        await using var migrateLegacy = connection.CreateCommand();
        migrateLegacy.Transaction = transaction;
        migrateLegacy.CommandText = "UPDATE RegisteredModels SET StructuredOutputMode = 1 WHERE SupportsStructuredJsonSchema = 1;";
        await migrateLegacy.ExecuteNonQueryAsync();
    }

    await using var select = connection.CreateCommand();
    select.Transaction = transaction;
    select.CommandText = """
        SELECT rm.Id
        FROM RegisteredModels rm
        INNER JOIN Providers p ON p.Id = rm.ProviderId
        WHERE p.Name = $providerName
          AND rm.ModelIdentifier = $modelIdentifier
          AND p.IsEnabled = 1
          AND rm.IsEnabled = 1;
        """;
    select.Parameters.AddWithValue("$providerName", providerName);
    select.Parameters.AddWithValue("$modelIdentifier", modelIdentifier);

    var modelIds = new List<string>();
    await using (var reader = await select.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            modelIds.Add(reader.GetString(0));
    }

    if (modelIds.Count != 1)
    {
        throw new InvalidOperationException(
            $"Expected exactly one enabled '{providerName}' model '{modelIdentifier}', found {modelIds.Count}; no database changes were made.");
    }

    await using var configureModel = connection.CreateCommand();
    configureModel.Transaction = transaction;
    configureModel.CommandText = "UPDATE RegisteredModels SET StructuredOutputMode = 2 WHERE Id = $modelId;";
    configureModel.Parameters.AddWithValue("$modelId", modelIds[0]);
    if (await configureModel.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException("Scene-beat analyzer model capability update failed; no database changes were made.");

    await using var upsert = connection.CreateCommand();
    upsert.Transaction = transaction;
    upsert.CommandText = """
        INSERT INTO FunctionModelDefaults (
            Id, FunctionName, ModelId, Temperature, TopP, MaxTokens, ThinkingMode,
            MaxConcurrentJobs, DurableJobLeaseSeconds, DurableJobPollIntervalMilliseconds,
            TransientRetryCount, TransientRetryDelaysSecondsJson, DiagnosticsRetentionDays,
            MaximumCatalogueEntries, UpdatedUtc)
        VALUES (
            $id, $functionName, $modelId, 0.2, 0.9, 4000, 2,
            3, 120, 250, 2, '[5,30]', 30, 8, $updatedUtc)
        ON CONFLICT(FunctionName) DO UPDATE SET
            ModelId = excluded.ModelId,
            Temperature = excluded.Temperature,
            TopP = excluded.TopP,
            MaxTokens = excluded.MaxTokens,
            ThinkingMode = excluded.ThinkingMode,
            MaxConcurrentJobs = excluded.MaxConcurrentJobs,
            DurableJobLeaseSeconds = excluded.DurableJobLeaseSeconds,
            DurableJobPollIntervalMilliseconds = excluded.DurableJobPollIntervalMilliseconds,
            TransientRetryCount = excluded.TransientRetryCount,
            TransientRetryDelaysSecondsJson = excluded.TransientRetryDelaysSecondsJson,
            DiagnosticsRetentionDays = excluded.DiagnosticsRetentionDays,
            MaximumCatalogueEntries = excluded.MaximumCatalogueEntries,
            UpdatedUtc = excluded.UpdatedUtc;
        """;
    upsert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
    upsert.Parameters.AddWithValue("$functionName", functionName);
    upsert.Parameters.AddWithValue("$modelId", modelIds[0]);
    upsert.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("o"));
    if (await upsert.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException("Scene-beat analyzer upsert failed; no database changes were made.");

    await transaction.CommitAsync();
    Console.WriteLine($"B-100 analyzer configured: {functionName} | {providerName} | {modelIdentifier}");
    return 0;
}

static async Task<int> ConfigureB100OpenRouterAnalyzerAsync(SqliteConnection connection)
{
    const string functionName = "RolePlaySceneBeatAnalyzer";
    const string modelDisplayName = "OP-deepseek-v4-flash-0731";
    const int structuredOutputMode = 2;
    const int maximumContextTokens = 1_310_720;
    const int maximumOutputTokens = 64_000;

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    await using var selectModel = connection.CreateCommand();
    selectModel.Transaction = transaction;
    selectModel.CommandText = "SELECT Id FROM RegisteredModels WHERE DisplayName = $displayName AND IsEnabled = 1;";
    selectModel.Parameters.AddWithValue("$displayName", modelDisplayName);
    var modelIds = new List<string>();
    await using (var reader = await selectModel.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            modelIds.Add(reader.GetString(0));
    }

    if (modelIds.Count != 1)
        throw new InvalidOperationException(
            $"Expected exactly one enabled registered model '{modelDisplayName}', found {modelIds.Count}; no database changes were made.");

    await using var updateModel = connection.CreateCommand();
    updateModel.Transaction = transaction;
    updateModel.CommandText = """
        UPDATE RegisteredModels
        SET StructuredOutputMode = $structuredOutputMode,
            MaximumContextTokens = $maximumContextTokens,
            MaximumOutputTokens = $maximumOutputTokens,
            SupportsThinkingControl = 0
        WHERE Id = $modelId AND DisplayName = $displayName AND IsEnabled = 1;
        """;
    updateModel.Parameters.AddWithValue("$structuredOutputMode", structuredOutputMode);
    updateModel.Parameters.AddWithValue("$maximumContextTokens", maximumContextTokens);
    updateModel.Parameters.AddWithValue("$maximumOutputTokens", maximumOutputTokens);
    updateModel.Parameters.AddWithValue("$modelId", modelIds[0]);
    updateModel.Parameters.AddWithValue("$displayName", modelDisplayName);
    if (await updateModel.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException("OpenRouter analyzer model capability update failed; no database changes were made.");

    await using var updateFunction = connection.CreateCommand();
    updateFunction.Transaction = transaction;
    updateFunction.CommandText = """
        UPDATE FunctionModelDefaults
        SET ModelId = $modelId,
            ThinkingMode = 2,
            UpdatedUtc = $updatedUtc
        WHERE FunctionName = $functionName;
        """;
    updateFunction.Parameters.AddWithValue("$modelId", modelIds[0]);
    updateFunction.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("o"));
    updateFunction.Parameters.AddWithValue("$functionName", functionName);
    if (await updateFunction.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException("OpenRouter analyzer function update failed; no database changes were made.");

    await transaction.CommitAsync();
    Console.WriteLine($"B-100 OpenRouter analyzer configured: {functionName} | {modelDisplayName}");
    return 0;
}

static async Task<int> ConfigureBigLustImageAsync(SqliteConnection connection)
{
    const string functionName = "RolePlaySceneImage";
    const string providerName = "RunPod Serverless BigLust";
    const string providerBaseUrl = "https://api.runpod.ai/v2/ovwnwol2o30grn";
    const string modelIdentifier = "bigLust_v16.safetensors";
    const string modelDisplayName = "BigLust v1.6 Serverless";
    const string modelArtifact = "Civitai 575395 / 1081768 / SHA-256 4C1E096B9493DBB5C0AB84FD80FD20AA64817544E565DDA95A45C637FC839AAF";
    const string providerNotes = "RunPod Serverless BigLust v1.6 endpoint img-biglust-serverless (worker-comfyui + IP-Adapter). API key resolved via CredentialReference 'runpod'.";
    const string modelNotes = "BigLust v1.6 SDXL T2I via RunPod serverless endpoint; checkpoint on network volume xkslgh6xo0.";
    // ReferenceConditioning (IP-Adapter PLUS FACE) is declared AND qualified for this serverless
    // endpoint so Model Manager resolves identity-on-create as Possible (declaration alone leaves it
    // Unqualified). The ProofId references the dated serverless IP-Adapter identity run artifact under
    // specs/image-generator-tests/biglust/runs/2026-09-02_132309-deanv6-front/.
    const string referenceConditioningDeclaration = "[\"ReferenceConditioning\"]";

    var now = DateTime.UtcNow.ToString("o");
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

    string providerId;
    await using (var selectProvider = connection.CreateCommand())
    {
        selectProvider.Transaction = transaction;
        selectProvider.CommandText = "SELECT Id FROM Providers WHERE Name = $name;";
        selectProvider.Parameters.AddWithValue("$name", providerName);
        var existingProviderId = await selectProvider.ExecuteScalarAsync();
        if (existingProviderId is string foundProviderId)
        {
            providerId = foundProviderId;
            await using var updateProvider = connection.CreateCommand();
            updateProvider.Transaction = transaction;
            updateProvider.CommandText = """
                UPDATE Providers
                SET BaseUrl = $baseUrl,
                    ProviderType = 0,
                    TimeoutSeconds = 900,
                    ImageCapability = 2,
                    ImageGenerationPath = '/v1/images/generations',
                    ContentPolicy = 2,
                    ImageProtocol = 2,
                    CredentialReference = 'runpod',
                    IsEnabled = 1,
                    Notes = $notes,
                    UpdatedUtc = $now
                WHERE Id = $providerId;
                """;
            updateProvider.Parameters.AddWithValue("$baseUrl", providerBaseUrl);
            updateProvider.Parameters.AddWithValue("$notes", providerNotes);
            updateProvider.Parameters.AddWithValue("$now", now);
            updateProvider.Parameters.AddWithValue("$providerId", providerId);
            await updateProvider.ExecuteNonQueryAsync();
        }
        else
        {
            providerId = Guid.NewGuid().ToString();
            await using var insertProvider = connection.CreateCommand();
            insertProvider.Transaction = transaction;
            insertProvider.CommandText = """
                INSERT INTO Providers (
                    Id, Name, ProviderType, BaseUrl, ChatCompletionsPath, TimeoutSeconds,
                    IsEnabled, CreatedUtc, UpdatedUtc, Notes, ImageCapability, ImageGenerationPath,
                    ContentPolicy, ImageProtocol, CredentialReference)
                VALUES (
                    $id, $name, 0, $baseUrl, '/v1/chat/completions', 900,
                    1, $now, $now, $notes, 2, '/v1/images/generations',
                    2, 2, 'runpod');
                """;
            insertProvider.Parameters.AddWithValue("$id", providerId);
            insertProvider.Parameters.AddWithValue("$name", providerName);
            insertProvider.Parameters.AddWithValue("$baseUrl", providerBaseUrl);
            insertProvider.Parameters.AddWithValue("$now", now);
            insertProvider.Parameters.AddWithValue("$notes", providerNotes);
            await insertProvider.ExecuteNonQueryAsync();
        }
    }

    var capabilityQualificationsJson =
        $"[{{\"Strategy\":\"ReferenceConditioning\",\"EndpointId\":\"{providerId}\",\"Qualified\":true,\"ProofId\":\"2026-09-02_132309-deanv6-front\"}}]";

    string modelId;
    await using (var selectModel = connection.CreateCommand())
    {
        selectModel.Transaction = transaction;
        selectModel.CommandText = "SELECT Id FROM RegisteredModels WHERE ProviderId = $providerId AND ModelIdentifier = $modelIdentifier;";
        selectModel.Parameters.AddWithValue("$providerId", providerId);
        selectModel.Parameters.AddWithValue("$modelIdentifier", modelIdentifier);
        var existingModelId = await selectModel.ExecuteScalarAsync();
        if (existingModelId is string foundModelId)
        {
            modelId = foundModelId;
            await using var updateModel = connection.CreateCommand();
            updateModel.Transaction = transaction;
            updateModel.CommandText = """
                UPDATE RegisteredModels
                SET DisplayName = $displayName,
                    ModelKind = 1,
                    SceneImageModelFamily = 2,
                    PromptDialect = 2,
                    IdentityMechanism = 'IpAdapter',
                    IdentityStrength = 0.8,
                    IdentityAdapterRef = 'PLUS FACE (portraits)',
                    SupportedIdentityStrategiesJson = $referenceConditioning,
                    SupportedVisualStrategiesJson = $referenceConditioning,
                    CapabilityQualificationsJson = $qualifications,
                    ArtifactRevision = $artifact,
                    Notes = $notes,
                    IsEnabled = 1
                WHERE Id = $modelId;
                """;
            updateModel.Parameters.AddWithValue("$referenceConditioning", referenceConditioningDeclaration);
            updateModel.Parameters.AddWithValue("$qualifications", capabilityQualificationsJson);
            updateModel.Parameters.AddWithValue("$displayName", modelDisplayName);
            updateModel.Parameters.AddWithValue("$artifact", modelArtifact);
            updateModel.Parameters.AddWithValue("$notes", modelNotes);
            updateModel.Parameters.AddWithValue("$modelId", modelId);
            await updateModel.ExecuteNonQueryAsync();
        }
        else
        {
            modelId = Guid.NewGuid().ToString();
            await using var insertModel = connection.CreateCommand();
            insertModel.Transaction = transaction;
            insertModel.CommandText = """
                INSERT INTO RegisteredModels (
                    Id, ProviderId, ModelIdentifier, DisplayName, IsEnabled, CreatedUtc,
                    ContextWindowSize, Quantization, ParameterCount, Notes, SupportsThinkingControl,
                    ModelKind, IdentityMechanism, IdentityStrength, IdentityAdapterRef, ArtifactRevision,
                    SceneImageModelFamily, PromptDialect,
                    SupportedIdentityStrategiesJson, SupportedVisualStrategiesJson, CapabilityQualificationsJson)
                VALUES (
                    $id, $providerId, $modelIdentifier, $displayName, 1, $now,
                    0, '', '', $notes, 0,
                    1, 'IpAdapter', 0.8, 'PLUS FACE (portraits)', $artifact,
                    2, 2,
                    $referenceConditioning, $referenceConditioning, $qualifications);
                """;
            insertModel.Parameters.AddWithValue("$referenceConditioning", referenceConditioningDeclaration);
            insertModel.Parameters.AddWithValue("$qualifications", capabilityQualificationsJson);
            insertModel.Parameters.AddWithValue("$id", modelId);
            insertModel.Parameters.AddWithValue("$providerId", providerId);
            insertModel.Parameters.AddWithValue("$modelIdentifier", modelIdentifier);
            insertModel.Parameters.AddWithValue("$displayName", modelDisplayName);
            insertModel.Parameters.AddWithValue("$now", now);
            insertModel.Parameters.AddWithValue("$notes", modelNotes);
            insertModel.Parameters.AddWithValue("$artifact", modelArtifact);
            await insertModel.ExecuteNonQueryAsync();
        }
    }

    await using (var upsertFunction = connection.CreateCommand())
    {
        upsertFunction.Transaction = transaction;
        upsertFunction.CommandText = """
            INSERT INTO FunctionModelDefaults (
                Id, FunctionName, ModelId, Temperature, TopP, MaxTokens, ThinkingMode, UpdatedUtc)
            VALUES (
                $id, $functionName, $modelId, 0.7, 0.9, 8000, 0, $now)
            ON CONFLICT(FunctionName) DO UPDATE SET
                ModelId = excluded.ModelId,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        upsertFunction.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        upsertFunction.Parameters.AddWithValue("$functionName", functionName);
        upsertFunction.Parameters.AddWithValue("$modelId", modelId);
        upsertFunction.Parameters.AddWithValue("$now", now);
        if (await upsertFunction.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("BigLust image function upsert failed; no database changes were made.");
    }

    await using (var disableJuggernaut = connection.CreateCommand())
    {
        disableJuggernaut.Transaction = transaction;
        disableJuggernaut.CommandText = "UPDATE RegisteredModels SET IsEnabled = 0 WHERE ModelIdentifier = 'juggernautXL_ragnarok.safetensors';";
        await disableJuggernaut.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
    Console.WriteLine($"BigLust image configured: {functionName} | {providerName} | {modelIdentifier} (Sdxl / SdxlNaturalLanguage)");
    return 0;
}

/// <summary>
/// Registers the local WOOD-GAME-MAIN ComfyUI host (RTX 5080, port 8188) as an additive
/// ImageProtocol.ComfyUi provider and registers its SDXL checkpoints as enabled image models.
/// This is NOT a RunPod pod: it is a direct ComfyUI HTTP endpoint on the dev machine itself and
/// can replace OR run alongside the RunPod Serverless image endpoints. Additive only: it never
/// repoints function defaults and never disables an existing provider/model.
///
/// FLUX.1-dev fp8 is registered as a present-but-DISABLED row: the app's SceneImageModelFamily has
/// no Flux value and the ComfyUI client only builds Pony/SDXL workflows, so an enabled FLUX row
/// would route an SDXL workflow at a FLUX checkpoint and fail. It is added disabled so Model Manager
/// records that the checkpoint exists locally until the B-112 Flux-family code slice lands.
///
/// Idempotent: re-running updates BaseUrl and the model rows in place. Base URL is a required
/// argument (e.g. http://127.0.0.1:8188 on the ComfyUI host, http://192.168.0.16:8188 from other hosts).
/// </summary>
static async Task<int> ConfigureLocalComfyUiAsync(SqliteConnection connection, string baseUrl)
{
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        throw new ArgumentException($"Base URL '{baseUrl}' must be an absolute http(s) URL; no database changes were made.");

    const string providerName = "Local ComfyUI (WOOD-GAME-MAIN 5080)";
    const string providerNotes =
        "Direct ComfyUI 0.34.0 on WOOD-GAME-MAIN (RTX 5080 16 GB, 192.168.0.16:8188) hosting FLUX.1-dev fp8 + "
        + "Juggernaut XL Ragnarok + BigLust v1.6 + Pony V6 XL + Pony Realism v2.3 ULTRA checkpoints and the "
        + "IP-Adapter/PuLID/FaceID identity stack. ImageProtocol=ComfyUi (direct /prompt), NOT a RunPod pod "
        + "and NOT serverless. Additive alternative to the RunPod Serverless image endpoints.";

    // (modelIdentifier, displayName, SceneImageModelFamily, SceneImagePromptDialect, enabled, notes, referenceConditioningProofId)
    // proofId is non-null only for models with a passing local IP-Adapter PLUS FACE identity proof:
    // juggernaut/biglust = identity-two-character run 20260908-local-sdxl-ipadapter (all cells PASS);
    // ponyRealism = local ponyrealism-ipadapter proof 2026-09-08 (Dean front likeness held). Pony V6
    // (mechanism loads but off-model in the identity matrix) and FLUX (no IP-Adapter PLUS FACE path)
    // intentionally stay unqualified.
    var models = new[]
    {
        ("juggernautXL_ragnarok.safetensors", "Juggernaut XL Ragnarok (Local ComfyUI)", 2, 2, true,
            "Local Juggernaut XL Ragnarok checkpoint on WOOD-GAME-MAIN ComfyUI (Sdxl / SdxlNaturalLanguage).",
            (string?)"20260908-local-sdxl-ipadapter-juggernaut"),
        ("bigLust_v16.safetensors", "BigLust v1.6 (Local ComfyUI)", 2, 2, true,
            "Local BigLust v1.6 checkpoint on WOOD-GAME-MAIN ComfyUI (Sdxl / SdxlNaturalLanguage).",
            (string?)"20260908-local-sdxl-ipadapter-biglust"),
        ("ponyDiffusionV6XL_v6.safetensors", "Pony V6 XL (Local ComfyUI)", 1, 1, true,
            "Local Pony V6 XL checkpoint on WOOD-GAME-MAIN ComfyUI (Pony / PonyV6Tags).",
            (string?)null),
        ("ponyRealism_V23ULTRA.safetensors", "Pony Realism v2.3 ULTRA (Local ComfyUI)", 1, 1, true,
            "Local Pony Realism v2.3 ULTRA (Civitai 372465 / version 1920896) photoreal Pony-architecture "
            + "checkpoint on WOOD-GAME-MAIN ComfyUI (Pony / PonyV6Tags). Additive image model - NOT the "
            + "RolePlaySceneImage default.",
            (string?)"ponyrealism-ipadapter-proof-2026-09-08"),
        ("flux1-dev-fp8.safetensors", "FLUX.1-dev fp8 (Local ComfyUI)", 4, 4, true,
            "Local FLUX.1-dev fp8 checkpoint on WOOD-GAME-MAIN ComfyUI (Flux / FluxNaturalLanguage). "
            + "Additive image model — NOT the RolePlaySceneImage default.",
            (string?)null),
    };

    var now = DateTime.UtcNow.ToString("o");
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

    string providerId;
    await using (var selectProvider = connection.CreateCommand())
    {
        selectProvider.Transaction = transaction;
        selectProvider.CommandText = "SELECT Id FROM Providers WHERE Name = $name;";
        selectProvider.Parameters.AddWithValue("$name", providerName);
        var existingProviderId = await selectProvider.ExecuteScalarAsync();
        if (existingProviderId is string foundProviderId)
        {
            providerId = foundProviderId;
            await using var updateProvider = connection.CreateCommand();
            updateProvider.Transaction = transaction;
            updateProvider.CommandText = """
                UPDATE Providers
                SET BaseUrl = $baseUrl,
                    ProviderType = 0,
                    TimeoutSeconds = 600,
                    ImageCapability = 2,
                    ImageGenerationPath = '/prompt',
                    ContentPolicy = 2,
                    ImageProtocol = 1,
                    CredentialReference = NULL,
                    LifecycleStrategyIdentifier = NULL,
                    ReadinessPath = NULL,
                    ReadinessSuccessContractJson = NULL,
                    IsEnabled = 1,
                    Notes = $notes,
                    UpdatedUtc = $now
                WHERE Id = $providerId;
                """;
            updateProvider.Parameters.AddWithValue("$baseUrl", baseUrl);
            updateProvider.Parameters.AddWithValue("$notes", providerNotes);
            updateProvider.Parameters.AddWithValue("$now", now);
            updateProvider.Parameters.AddWithValue("$providerId", providerId);
            if (await updateProvider.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("Local ComfyUI provider update failed; no database changes were made.");
        }
        else
        {
            providerId = Guid.NewGuid().ToString();
            await using var insertProvider = connection.CreateCommand();
            insertProvider.Transaction = transaction;
            insertProvider.CommandText = """
                INSERT INTO Providers (
                    Id, Name, ProviderType, BaseUrl, ChatCompletionsPath, TimeoutSeconds,
                    IsEnabled, CreatedUtc, UpdatedUtc, Notes, ImageCapability, ImageGenerationPath,
                    ContentPolicy, ImageProtocol)
                VALUES (
                    $id, $name, 0, $baseUrl, '/v1/chat/completions', 600,
                    1, $now, $now, $notes, 2, '/prompt',
                    2, 1);
                """;
            insertProvider.Parameters.AddWithValue("$id", providerId);
            insertProvider.Parameters.AddWithValue("$name", providerName);
            insertProvider.Parameters.AddWithValue("$baseUrl", baseUrl);
            insertProvider.Parameters.AddWithValue("$now", now);
            insertProvider.Parameters.AddWithValue("$notes", providerNotes);
            await insertProvider.ExecuteNonQueryAsync();
        }
    }

    foreach (var (modelIdentifier, displayName, family, dialect, enabled, modelNotes, proofId) in models)
    {
        string modelId;
        await using (var selectModel = connection.CreateCommand())
        {
            selectModel.Transaction = transaction;
            selectModel.CommandText = "SELECT Id FROM RegisteredModels WHERE ProviderId = $providerId AND ModelIdentifier = $modelIdentifier;";
            selectModel.Parameters.AddWithValue("$providerId", providerId);
            selectModel.Parameters.AddWithValue("$modelIdentifier", modelIdentifier);
            var existingModelId = await selectModel.ExecuteScalarAsync();
            if (existingModelId is string foundModelId)
            {
                modelId = foundModelId;
                await using var updateModel = connection.CreateCommand();
                updateModel.Transaction = transaction;
                updateModel.CommandText = """
                    UPDATE RegisteredModels
                    SET DisplayName = $displayName,
                        ModelKind = 1,
                        SceneImageModelFamily = $family,
                        PromptDialect = $dialect,
                        Notes = $notes,
                        IsEnabled = $enabled
                    WHERE Id = $modelId;
                    """;
                updateModel.Parameters.AddWithValue("$displayName", displayName);
                updateModel.Parameters.AddWithValue("$family", family);
                updateModel.Parameters.AddWithValue("$dialect", dialect);
                updateModel.Parameters.AddWithValue("$notes", modelNotes);
                updateModel.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
                updateModel.Parameters.AddWithValue("$modelId", modelId);
                await updateModel.ExecuteNonQueryAsync();
            }
            else
            {
                modelId = Guid.NewGuid().ToString();
                await using var insertModel = connection.CreateCommand();
                insertModel.Transaction = transaction;
                insertModel.CommandText = """
                    INSERT INTO RegisteredModels (
                        Id, ProviderId, ModelIdentifier, DisplayName, IsEnabled, CreatedUtc,
                        ContextWindowSize, Quantization, ParameterCount, Notes, SupportsThinkingControl,
                        ModelKind, SceneImageModelFamily, PromptDialect)
                    VALUES (
                        $id, $providerId, $modelIdentifier, $displayName, $enabled, $now,
                        0, '', '', $notes, 0,
                        1, $family, $dialect);
                    """;
                insertModel.Parameters.AddWithValue("$id", modelId);
                insertModel.Parameters.AddWithValue("$providerId", providerId);
                insertModel.Parameters.AddWithValue("$modelIdentifier", modelIdentifier);
                insertModel.Parameters.AddWithValue("$displayName", displayName);
                insertModel.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
                insertModel.Parameters.AddWithValue("$now", now);
                insertModel.Parameters.AddWithValue("$notes", modelNotes);
                insertModel.Parameters.AddWithValue("$family", family);
                insertModel.Parameters.AddWithValue("$dialect", dialect);
                await insertModel.ExecuteNonQueryAsync();
            }
        }
        if (!string.IsNullOrWhiteSpace(proofId))
        {
            await ApplyLocalIdentityQualificationAsync(connection, transaction, modelId, providerId, proofId);
        }
    }

    await transaction.CommitAsync();
    var enabledList = string.Join(", ", models.Where(m => m.Item5).Select(m => m.Item1));
    Console.WriteLine($"Local ComfyUI configured: {providerName} | {baseUrl} | enabled={enabledList}");
    return 0;
}

/// <summary>
/// Declares + qualifies IP-Adapter PLUS FACE <c>ReferenceConditioning</c> on a local ComfyUI model
/// that has a passing local identity proof. Merges (never clobbers) existing visual-strategy and
/// capability-qualification JSON, so ControlNet/PoseControlNet entries already on the row survive.
/// </summary>
static async Task ApplyLocalIdentityQualificationAsync(
    SqliteConnection connection,
    SqliteTransaction transaction,
    string modelId,
    string providerId,
    string proofId)
{
    var visualJson = "[]";
    var qualificationsJson = "[]";
    await using (var select = connection.CreateCommand())
    {
        select.Transaction = transaction;
        select.CommandText = "SELECT SupportedVisualStrategiesJson, CapabilityQualificationsJson FROM RegisteredModels WHERE Id = $modelId;";
        select.Parameters.AddWithValue("$modelId", modelId);
        await using var reader = await select.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            visualJson = reader.IsDBNull(0) ? "[]" : reader.GetString(0);
            qualificationsJson = reader.IsDBNull(1) ? "[]" : reader.GetString(1);
        }
    }

    var visual = JsonNode.Parse(visualJson) as JsonArray ?? new JsonArray();
    var hasVisual = visual.Any(node => node is not null
        && string.Equals(node.GetValue<string>(), "ReferenceConditioning", StringComparison.OrdinalIgnoreCase));
    if (!hasVisual)
        visual.Add("ReferenceConditioning");

    var qualifications = JsonNode.Parse(qualificationsJson) as JsonArray ?? new JsonArray();
    var matched = false;
    foreach (var node in qualifications)
    {
        if (node is not JsonObject entry) continue;
        var strategy = entry["Strategy"]?.GetValue<string>();
        var endpointId = entry["EndpointId"]?.GetValue<string>();
        if (string.Equals(strategy, "ReferenceConditioning", StringComparison.OrdinalIgnoreCase)
            && string.Equals(endpointId, providerId, StringComparison.OrdinalIgnoreCase))
        {
            entry["Qualified"] = true;
            entry["ProofId"] = proofId;
            matched = true;
        }
    }
    if (!matched)
    {
        qualifications.Add(new JsonObject
        {
            ["Strategy"] = "ReferenceConditioning",
            ["EndpointId"] = providerId,
            ["Qualified"] = true,
            ["ProofId"] = proofId
        });
    }

    await using var update = connection.CreateCommand();
    update.Transaction = transaction;
    update.CommandText = """
        UPDATE RegisteredModels
        SET IdentityMechanism = 'IpAdapter',
            IdentityStrength = 0.8,
            IdentityAdapterRef = 'PLUS FACE (portraits)',
            SupportedIdentityStrategiesJson = '["ReferenceConditioning"]',
            SupportedVisualStrategiesJson = $visual,
            CapabilityQualificationsJson = $qualifications
        WHERE Id = $modelId;
        """;
    update.Parameters.AddWithValue("$visual", visual.ToJsonString());
    update.Parameters.AddWithValue("$qualifications", qualifications.ToJsonString());
    update.Parameters.AddWithValue("$modelId", modelId);
    if (await update.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException($"Local identity qualification update failed for model '{modelId}'; no database changes were made.");
}

static async Task<int> ConfigureQwenEditServerlessAsync(SqliteConnection connection)
{
    const string functionName = "RolePlaySceneImageEditor";
    const string providerName = "RunPod Qwen Edit Serverless";
    const string providerBaseUrl = "https://api.runpod.ai/v2/79wkn5jz5d5txx";
    const string modelIdentifier = "qwen-image-edit-rapid-aio-nsfw-v23";
    const string modelDisplayName = "Qwen Image Edit Rapid-AIO NSFW v23";
    const string diffusionModel = "Qwen-Rapid-AIO-NSFW-v23.safetensors";
    const string providerNotes = "RunPod Serverless Qwen Edit endpoint img-qwen-edit-serverless. API key resolved through CredentialReference 'runpod'.";
    const string modelNotes = "Phr00t Qwen Image Edit Rapid-AIO NSFW v23 merged checkpoint using the proven serverless ComfyUI workflow.";

    var now = DateTime.UtcNow.ToString("o");
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    await using var select = connection.CreateCommand();
    select.Transaction = transaction;
    select.CommandText = """
        SELECT f.ModelId, rm.ProviderId, p.BaseUrl
        FROM FunctionModelDefaults f
        INNER JOIN RegisteredModels rm ON rm.Id = f.ModelId
        INNER JOIN Providers p ON p.Id = rm.ProviderId
        WHERE f.FunctionName = $functionName;
        """;
    select.Parameters.AddWithValue("$functionName", functionName);

    string modelId;
    string providerId;
    string currentBaseUrl;
    await using (var reader = await select.ExecuteReaderAsync())
    {
        if (!await reader.ReadAsync())
            throw new InvalidOperationException(
                $"Function '{functionName}' has no configured provider/model; no database changes were made.");
        modelId = reader.GetString(0);
        providerId = reader.GetString(1);
        currentBaseUrl = reader.GetString(2);
    }

    var alreadyServerless = string.Equals(currentBaseUrl, providerBaseUrl, StringComparison.Ordinal);
    if (!alreadyServerless && !string.Equals(currentBaseUrl, "http://127.0.0.1:3002", StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"Qwen editor endpoint changed concurrently. Expected the legacy local endpoint or '{providerBaseUrl}', found '{currentBaseUrl}'; no database changes were made.");

    await using (var updateProvider = connection.CreateCommand())
    {
        updateProvider.Transaction = transaction;
        updateProvider.CommandText = """
            UPDATE Providers
            SET Name = $name,
                BaseUrl = $baseUrl,
                ProviderType = 0,
                ImageCapability = 2,
                ContentPolicy = 2,
                ImageProtocol = 2,
                TimeoutSeconds = 900,
                LifecycleStrategyIdentifier = 'Serverless',
                CredentialReference = 'runpod',
                IsEnabled = 1,
                Notes = $notes,
                UpdatedUtc = $now
            WHERE Id = $providerId;
            """;
        updateProvider.Parameters.AddWithValue("$name", providerName);
        updateProvider.Parameters.AddWithValue("$baseUrl", providerBaseUrl);
        updateProvider.Parameters.AddWithValue("$notes", providerNotes);
        updateProvider.Parameters.AddWithValue("$now", now);
        updateProvider.Parameters.AddWithValue("$providerId", providerId);
        if (await updateProvider.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("Qwen serverless provider update failed; no database changes were made.");
    }

    await using (var updateModel = connection.CreateCommand())
    {
        updateModel.Transaction = transaction;
        updateModel.CommandText = """
            UPDATE RegisteredModels
            SET ModelIdentifier = $modelIdentifier,
                DisplayName = $displayName,
                ModelKind = 1,
                ImageEditorDiffusionModel = $diffusionModel,
                ImageEditorSteps = 8,
                ImageEditorCfg = 1.0,
                ImageEditorSampler = 'euler_ancestral',
                ImageEditorScheduler = 'beta',
                ImageEditorDenoise = 1.0,
                ImageEditorAuraFlowShift = 3.1,
                ImageEditorCfgNormStrength = 1.0,
                Notes = $notes,
                IsEnabled = 1
            WHERE Id = $modelId
              AND ProviderId = $providerId;
            """;
        updateModel.Parameters.AddWithValue("$modelIdentifier", modelIdentifier);
        updateModel.Parameters.AddWithValue("$displayName", modelDisplayName);
        updateModel.Parameters.AddWithValue("$diffusionModel", diffusionModel);
        updateModel.Parameters.AddWithValue("$notes", modelNotes);
        updateModel.Parameters.AddWithValue("$modelId", modelId);
        updateModel.Parameters.AddWithValue("$providerId", providerId);
        if (await updateModel.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("Qwen serverless model update failed; no database changes were made.");
    }

    await transaction.CommitAsync();
    Console.WriteLine(
        $"Qwen editor configured: {functionName} | {providerName} | {modelIdentifier} | {providerBaseUrl} | ExistingIdsPreserved={providerId}/{modelId}");
    return 0;
}

static async Task<int> ConfigureQwenEditLocalAioAsync(SqliteConnection connection)
{
    const string functionName = "RolePlaySceneImageEditor";
    const string providerName = "Local ComfyUI (WOOD-GAME-MAIN 5080)";
    const string diffusionModel = "Qwen-Rapid-AIO-NSFW-v23.safetensors";
    const string graphKind = "MergedCheckpoint";
    const string displayName = "Qwen Image Edit Rapid-AIO NSFW v23 (Local ComfyUI)";
    const int steps = 8;
    const double cfg = 1.0;
    const string sampler = "euler_ancestral";
    const string scheduler = "beta";
    const double denoise = 1.0;
    const double auraFlowShift = 3.1;
    const double cfgNormStrength = 1.0;

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

    string providerId;
    long imageProtocol;
    await using (var selectProvider = connection.CreateCommand())
    {
        selectProvider.Transaction = transaction;
        selectProvider.CommandText = "SELECT Id, ImageProtocol FROM Providers WHERE Name = $name;";
        selectProvider.Parameters.AddWithValue("$name", providerName);
        await using var reader = await selectProvider.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"Provider '{providerName}' was not found; no database changes were made.");
        providerId = reader.GetString(0);
        imageProtocol = reader.GetInt64(1);
    }

    if (imageProtocol != 1)
        throw new InvalidOperationException($"Provider '{providerName}' is not a direct ComfyUI HTTP provider (ImageProtocol={imageProtocol}); the merged-checkpoint graph applies to ComfyUI editors. No database changes were made.");

    string modelId;
    string? currentDiffusionModel;
    string? currentGraphKind;
    string? currentDisplayName;
    long? currentSteps;
    double? currentCfg;
    string? currentSampler;
    string? currentScheduler;
    double? currentDenoise;
    double? currentAuraFlowShift;
    double? currentCfgNormStrength;
    await using (var selectModel = connection.CreateCommand())
    {
        selectModel.Transaction = transaction;
        selectModel.CommandText = """
            SELECT Id, DisplayName, ImageEditorDiffusionModel, ImageEditorGraphKind, ImageEditorSteps, ImageEditorCfg,
                   ImageEditorSampler, ImageEditorScheduler, ImageEditorDenoise, ImageEditorAuraFlowShift, ImageEditorCfgNormStrength
            FROM RegisteredModels
            WHERE ProviderId = $providerId AND ImageEditorDiffusionModel IS NOT NULL;
            """;
        selectModel.Parameters.AddWithValue("$providerId", providerId);
        var candidateIds = new List<string>();
        await using var reader = await selectModel.ExecuteReaderAsync();
        modelId = string.Empty;
        currentDiffusionModel = null;
        currentGraphKind = null;
        currentDisplayName = null;
        currentSteps = null;
        currentCfg = null;
        currentSampler = null;
        currentScheduler = null;
        currentDenoise = null;
        currentAuraFlowShift = null;
        currentCfgNormStrength = null;
        while (await reader.ReadAsync())
        {
            candidateIds.Add(reader.GetString(0));
            modelId = reader.GetString(0);
            currentDisplayName = reader.GetString(1);
            currentDiffusionModel = reader.IsDBNull(2) ? null : reader.GetString(2);
            currentGraphKind = reader.IsDBNull(3) ? null : reader.GetString(3);
            currentSteps = reader.IsDBNull(4) ? null : reader.GetInt64(4);
            currentCfg = reader.IsDBNull(5) ? null : reader.GetDouble(5);
            currentSampler = reader.IsDBNull(6) ? null : reader.GetString(6);
            currentScheduler = reader.IsDBNull(7) ? null : reader.GetString(7);
            currentDenoise = reader.IsDBNull(8) ? null : reader.GetDouble(8);
            currentAuraFlowShift = reader.IsDBNull(9) ? null : reader.GetDouble(9);
            currentCfgNormStrength = reader.IsDBNull(10) ? null : reader.GetDouble(10);
        }
        if (candidateIds.Count != 1)
            throw new InvalidOperationException($"Expected exactly one editor model on provider '{providerName}'; found {candidateIds.Count}. No database changes were made.");
    }

    string? functionDefaultModelId;
    await using (var selectFunction = connection.CreateCommand())
    {
        selectFunction.Transaction = transaction;
        selectFunction.CommandText = "SELECT ModelId FROM FunctionModelDefaults WHERE FunctionName = $functionName;";
        selectFunction.Parameters.AddWithValue("$functionName", functionName);
        functionDefaultModelId = await selectFunction.ExecuteScalarAsync() as string;
    }

    if (!string.Equals(functionDefaultModelId, modelId, StringComparison.Ordinal))
        throw new InvalidOperationException($"'{functionName}' is not assigned to the local editor model ({modelId}); assign it in Model Manager before running this command. No database changes were made.");

    var alreadyConfigured =
        string.Equals(currentDiffusionModel, diffusionModel, StringComparison.Ordinal) &&
        string.Equals(currentGraphKind, graphKind, StringComparison.Ordinal) &&
        string.Equals(currentDisplayName, displayName, StringComparison.Ordinal) &&
        currentSteps == steps &&
        currentCfg == cfg &&
        string.Equals(currentSampler, sampler, StringComparison.Ordinal) &&
        string.Equals(currentScheduler, scheduler, StringComparison.Ordinal) &&
        currentDenoise == denoise &&
        currentAuraFlowShift == auraFlowShift &&
        currentCfgNormStrength == cfgNormStrength;

    if (alreadyConfigured)
    {
        await transaction.RollbackAsync();
        Console.WriteLine($"Local Qwen editor already configured: {providerName} | {diffusionModel} | {graphKind} | {steps} steps / CFG {cfg} / {sampler} / {scheduler}. No changes made.");
        return 0;
    }

    await using (var update = connection.CreateCommand())
    {
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE RegisteredModels
            SET DisplayName = $displayName,
                ImageEditorDiffusionModel = $diffusionModel,
                ImageEditorGraphKind = $graphKind,
                ImageEditorSteps = $steps,
                ImageEditorCfg = $cfg,
                ImageEditorSampler = $sampler,
                ImageEditorScheduler = $scheduler,
                ImageEditorDenoise = $denoise,
                ImageEditorAuraFlowShift = $auraFlowShift,
                ImageEditorCfgNormStrength = $cfgNormStrength
            WHERE Id = $modelId
              AND ProviderId = $providerId;
            """;
        update.Parameters.AddWithValue("$displayName", displayName);
        update.Parameters.AddWithValue("$diffusionModel", diffusionModel);
        update.Parameters.AddWithValue("$graphKind", graphKind);
        update.Parameters.AddWithValue("$steps", steps);
        update.Parameters.AddWithValue("$cfg", cfg);
        update.Parameters.AddWithValue("$sampler", sampler);
        update.Parameters.AddWithValue("$scheduler", scheduler);
        update.Parameters.AddWithValue("$denoise", denoise);
        update.Parameters.AddWithValue("$auraFlowShift", auraFlowShift);
        update.Parameters.AddWithValue("$cfgNormStrength", cfgNormStrength);
        update.Parameters.AddWithValue("$modelId", modelId);
        update.Parameters.AddWithValue("$providerId", providerId);
        if (await update.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("Local Qwen editor update failed; no database changes were made.");
    }

    await transaction.CommitAsync();
    Console.WriteLine(
        $"Local Qwen editor configured: {functionName} | {providerName} | {diffusionModel} | graph={graphKind} | {steps} steps / CFG {cfg} / {sampler} / {scheduler} | ModelId={modelId}");
    return 0;
}

/// <summary>
/// Sets the identity conditioning strength for an image model. Lowering the strength trades a little
/// face fidelity for much stronger prompt adherence (scene/setting/wardrobe). Validates the target
/// model exists and is an image model before updating; fails without changes otherwise.
/// </summary>
static async Task<int> SetIdentityStrengthAsync(
    SqliteConnection connection,
    string modelIdentifier,
    string strengthArg)
{
    if (!double.TryParse(strengthArg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var strength)
        || strength < 0 || strength > 2)
    {
        throw new InvalidOperationException($"Identity strength must be a number between 0 and 2; got '{strengthArg}'.");
    }

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

    string? modelId;
    await using (var select = connection.CreateCommand())
    {
        select.Transaction = transaction;
        select.CommandText = "SELECT Id FROM RegisteredModels WHERE ModelIdentifier = $identifier AND ModelKind = 1;";
        select.Parameters.AddWithValue("$identifier", modelIdentifier);
        modelId = await select.ExecuteScalarAsync() as string;
    }

    if (string.IsNullOrWhiteSpace(modelId))
        throw new InvalidOperationException($"No image model found for identifier '{modelIdentifier}'; no changes were made.");

    await using (var update = connection.CreateCommand())
    {
        update.Transaction = transaction;
        update.CommandText = "UPDATE RegisteredModels SET IdentityStrength = $strength WHERE Id = $modelId;";
        update.Parameters.AddWithValue("$strength", strength);
        update.Parameters.AddWithValue("$modelId", modelId);
        if (await update.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("Identity strength update failed; no database changes were made.");
    }

    await transaction.CommitAsync();
    Console.WriteLine($"Identity strength updated: {modelIdentifier} -> {strength}");
    return 0;
}

/// <summary>
/// Updates a scenario character's body-figure fields (Weight, BustSize, ButtSize) inside the
/// scenario's nested PayloadJson. Targeted, validated, transactional — fails with no changes if the
/// scenario or character is missing. Keeps the legacy BustMeasurement alias in sync so it cannot
/// clobber the canonical value on the next deserialization.
/// </summary>
static async Task<int> UpdateCharacterFigureAsync(
    SqliteConnection connection,
    string scenarioId,
    string characterName,
    string weight,
    string bustSize,
    string buttSize)
{
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

    string? payloadJson;
    await using (var select = connection.CreateCommand())
    {
        select.Transaction = transaction;
        select.CommandText = "SELECT PayloadJson FROM Scenarios WHERE Id = $scenarioId;";
        select.Parameters.AddWithValue("$scenarioId", scenarioId);
        payloadJson = await select.ExecuteScalarAsync() as string;
    }

    if (string.IsNullOrWhiteSpace(payloadJson))
        throw new InvalidOperationException($"Scenario '{scenarioId}' was not found; no changes were made.");

    var root = JsonNode.Parse(payloadJson)
        ?? throw new InvalidOperationException("Scenario PayloadJson is not valid JSON; no changes were made.");
    var characters = root["Characters"] as JsonArray
        ?? throw new InvalidOperationException("Scenario PayloadJson has no Characters array; no changes were made.");

    JsonObject? character = null;
    foreach (var node in characters)
    {
        if (node is JsonObject obj
            && string.Equals(obj["Name"]?.GetValue<string>(), characterName, StringComparison.OrdinalIgnoreCase))
        {
            character = obj;
            break;
        }
    }
    if (character is null)
        throw new InvalidOperationException($"Character '{characterName}' was not found in scenario '{scenarioId}'; no changes were made.");

    if (character["PhysicalAttributes"] is not JsonObject physical)
    {
        physical = new JsonObject();
        character["PhysicalAttributes"] = physical;
    }

    physical["Weight"] = weight;
    physical["BustSize"] = bustSize;
    physical["ButtSize"] = buttSize;
    physical["BustMeasurement"] = bustSize;

    var updatedJson = root.ToJsonString();

    await using (var update = connection.CreateCommand())
    {
        update.Transaction = transaction;
        update.CommandText = "UPDATE Scenarios SET PayloadJson = $payload, UpdatedUtc = $now WHERE Id = $scenarioId;";
        update.Parameters.AddWithValue("$payload", updatedJson);
        update.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        update.Parameters.AddWithValue("$scenarioId", scenarioId);
        if (await update.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("Character figure update failed; no database changes were made.");
    }

    await transaction.CommitAsync();
    Console.WriteLine($"Character figure updated: {characterName} in {scenarioId} -> weight={weight}, bust={bustSize}, butt={buttSize}");
    return 0;
}

/// <summary>
/// Configures the TogetherAI API image models (openai/gpt-image-2, Seedream-4.0,
/// google/imagen-4.0-preview) as plain API natural-language scene-image models. These are
/// OpenAI-compatible images-endpoint requests, not Pony/SDXL checkpoint models, so they get the
/// explicit Api family + NaturalLanguage dialect (values 3/3) that the render pipeline routes as a
/// simple image request.
/// </summary>
static async Task<int> ConfigureApiImageModelsAsync(SqliteConnection connection)
{
    var now = DateTime.UtcNow.ToString("o");
    var targets = new (string Identifier, string Label)[]
    {
        ("openai/gpt-image-2", "GPT-Image-2"),
        ("ByteDance-Seed/Seedream-4.0", "Seedream-4.0"),
        ("google/imagen-4.0-preview", "Imagen-4.0-preview")
    };

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    foreach (var (identifier, label) in targets)
    {
        string? modelId;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT Id FROM RegisteredModels WHERE ModelIdentifier = $identifier AND ModelKind = 1;";
            select.Parameters.AddWithValue("$identifier", identifier);
            modelId = (await select.ExecuteScalarAsync()) as string;
        }
        if (modelId is null)
            throw new InvalidOperationException($"API image model '{label}' ({identifier}) was not found; no database changes were made.");

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE RegisteredModels
            SET SceneImageModelFamily = 3,
                PromptDialect = 3
            WHERE Id = $modelId;
            """;
        update.Parameters.AddWithValue("$modelId", modelId);
        if (await update.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException($"API image model '{label}' update failed; no database changes were made.");
    }

    await transaction.CommitAsync();
    Console.WriteLine($"API image models configured: SceneImageModelFamily=Api, PromptDialect=NaturalLanguage (gpt-image-2, Seedream-4.0, Imagen-4.0) | UpdatedUtc={now}");
    return 0;
}

/// <summary>
/// Verifies and repairs the TogetherAI API image-model catalog (2026-09-01).
/// - Disables 'google/imagen-4.0-preview': TogetherAI rejects it with HTTP 400 "Invalid value for
///   'model' parameter" on /v1/images/generations, so it can never render.
/// - Upserts the TogetherAI image models verified to generate at 1024x1024: google/flash-image-3.1,
///   Qwen/Qwen-Image-2.0-Pro, black-forest-labs/FLUX.1.1-pro (explicit Api / NaturalLanguage).
/// Idempotent: re-running only re-asserts the same end state.
/// </summary>
static async Task<int> ConfigureApiImageCatalogAsync(SqliteConnection connection)
{
    const string providerName = "TogetherAI";
    var disabledModelIdentifiers = new[] { "google/imagen-4.0-preview" };
    var catalog = new (string Identifier, string DisplayName)[]
    {
        ("google/flash-image-3.1", "Google Flash Image 3.1"),
        ("Qwen/Qwen-Image-2.0-Pro", "Qwen Image 2.0 Pro"),
        ("black-forest-labs/FLUX.1.1-pro", "FLUX.1.1 Pro")
    };

    var now = DateTime.UtcNow.ToString("o");
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

    string? providerId;
    await using (var selectProvider = connection.CreateCommand())
    {
        selectProvider.Transaction = transaction;
        selectProvider.CommandText = "SELECT Id FROM Providers WHERE Name = $name;";
        selectProvider.Parameters.AddWithValue("$name", providerName);
        providerId = (await selectProvider.ExecuteScalarAsync()) as string;
    }
    if (providerId is null)
        throw new InvalidOperationException($"Provider '{providerName}' was not found; no database changes were made.");

    foreach (var identifier in disabledModelIdentifiers)
    {
        await using var disable = connection.CreateCommand();
        disable.Transaction = transaction;
        disable.CommandText = "UPDATE RegisteredModels SET IsEnabled = 0 WHERE ModelIdentifier = $identifier AND ModelKind = 1;";
        disable.Parameters.AddWithValue("$identifier", identifier);
        if (await disable.ExecuteNonQueryAsync() == 0)
            throw new InvalidOperationException($"Image model '{identifier}' was not found to disable; no database changes were made.");
    }

    foreach (var (identifier, displayName) in catalog)
    {
        string? modelId;
        await using (var selectModel = connection.CreateCommand())
        {
            selectModel.Transaction = transaction;
            selectModel.CommandText = "SELECT Id FROM RegisteredModels WHERE ProviderId = $providerId AND ModelIdentifier = $identifier;";
            selectModel.Parameters.AddWithValue("$providerId", providerId);
            selectModel.Parameters.AddWithValue("$identifier", identifier);
            modelId = (await selectModel.ExecuteScalarAsync()) as string;
        }

        if (modelId is null)
        {
            modelId = Guid.NewGuid().ToString();
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO RegisteredModels (
                    Id, ProviderId, ModelIdentifier, DisplayName, IsEnabled, CreatedUtc,
                    ModelKind, SceneImageModelFamily, PromptDialect)
                VALUES (
                    $id, $providerId, $identifier, $displayName, 1, $now,
                    1, 3, 3);
                """;
            insert.Parameters.AddWithValue("$id", modelId);
            insert.Parameters.AddWithValue("$providerId", providerId);
            insert.Parameters.AddWithValue("$identifier", identifier);
            insert.Parameters.AddWithValue("$displayName", displayName);
            insert.Parameters.AddWithValue("$now", now);
            await insert.ExecuteNonQueryAsync();
        }
        else
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE RegisteredModels
                SET DisplayName = $displayName,
                    ModelKind = 1,
                    SceneImageModelFamily = 3,
                    PromptDialect = 3,
                    IsEnabled = 1
                WHERE Id = $modelId;
                """;
            update.Parameters.AddWithValue("$displayName", displayName);
            update.Parameters.AddWithValue("$modelId", modelId);
            await update.ExecuteNonQueryAsync();
        }
    }

    await transaction.CommitAsync();
    Console.WriteLine("API image catalog configured: disabled google/imagen-4.0-preview; added flash-image-3.1, Qwen-Image-2.0-Pro, FLUX.1.1-pro (Api / NaturalLanguage).");
    return 0;
}

static async Task<int> SettleStaleProductionPlanAsync(SqliteConnection connection, string planId)
{
    planId = planId.Trim();
    var now = DateTime.UtcNow.ToString("o");
    const string code = "settled_unclassified_handler_failure";
    const string message = "Settled: the durable handler failed before the attempt was marked failed.";

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

    string? attemptId = null;
    string? planStatus = null;
    await using (var load = connection.CreateCommand())
    {
        load.Transaction = transaction;
        load.CommandText = "SELECT CurrentAttemptId, Status FROM SceneBeatProductionPlans WHERE Id = $planId;";
        load.Parameters.AddWithValue("$planId", planId);
        await using var reader = await load.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"Beat Production Plan '{planId}' was not found; no changes were made.");
        attemptId = reader.IsDBNull(0) ? null : reader.GetString(0);
        planStatus = reader.GetString(1);
    }

    if (string.IsNullOrWhiteSpace(attemptId))
        throw new InvalidOperationException($"Beat Production Plan '{planId}' has no current attempt; no changes were made.");
    if (planStatus is not ("Pending" or "Processing"))
        throw new InvalidOperationException($"Beat Production Plan '{planId}' is '{planStatus}'; only Pending/Processing plans can be settled. No changes were made.");

    await using (var failAttempt = connection.CreateCommand())
    {
        failAttempt.Transaction = transaction;
        failAttempt.CommandText = """
            UPDATE SceneBeatProductionAttempts
            SET Status = 'Failed', ValidationCode = $code,
                ValidationDetailsJson = json_object('message', $message),
                CompletedUtc = $now, UpdatedUtc = $now
            WHERE Id = $attemptId AND Status IN ('Queued', 'Processing');
            """;
        failAttempt.Parameters.AddWithValue("$code", code);
        failAttempt.Parameters.AddWithValue("$message", message);
        failAttempt.Parameters.AddWithValue("$attemptId", attemptId);
        failAttempt.Parameters.AddWithValue("$now", now);
        if (await failAttempt.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException($"Attempt '{attemptId}' is not Queued/Processing; no changes were made.");
    }

    await using (var failPlan = connection.CreateCommand())
    {
        failPlan.Transaction = transaction;
        failPlan.CommandText = """
            UPDATE SceneBeatProductionPlans
            SET Status = 'Failed', ErrorCode = $code, ErrorMessage = $message,
                CompletedUtc = $now, UpdatedUtc = $now
            WHERE Id = $planId AND Status IN ('Pending', 'Processing');
            """;
        failPlan.Parameters.AddWithValue("$code", code);
        failPlan.Parameters.AddWithValue("$message", message);
        failPlan.Parameters.AddWithValue("$planId", planId);
        failPlan.Parameters.AddWithValue("$now", now);
        if (await failPlan.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException($"Plan '{planId}' failed to settle; no changes were made.");
    }

    await transaction.CommitAsync();
    Console.WriteLine($"Beat Production Plan settled: {planId} | attempt {attemptId} -> Failed ({code})");
    return 0;
}

static async Task<int> ReconcileTurnMembershipsAsync(SqliteConnection connection, string sessionId)
{
    sessionId = sessionId.Trim();
    var now = DateTime.UtcNow.ToString("o");
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

    // Live interaction ids for the session.
    var liveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var live = connection.CreateCommand())
    {
        live.Transaction = transaction;
        live.CommandText = """
            SELECT json_extract(je.value, '$.id')
            FROM Sessions s, json_each(s.PayloadJson, '$.interactions') je
            WHERE s.Id = $sessionId;
            """;
        live.Parameters.AddWithValue("$sessionId", sessionId);
        await using var reader = await live.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(id)) liveIds.Add(id);
        }
    }
    if (liveIds.Count == 0)
        throw new InvalidOperationException($"Session '{sessionId}' has no persisted interactions; no changes were made.");

    // Replacements derived from delete-and-promote debug events (originalId -> promotedId).
    var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    await using (var promote = connection.CreateCommand())
    {
        promote.Transaction = transaction;
        promote.CommandText = """
            SELECT MetadataJson
            FROM RolePlayDebugEvents
            WHERE SessionId = $sessionId
              AND EventKind = 'CommandExecuted'
              AND Summary = 'Original interaction deleted; first alternative promoted';
            """;
        promote.Parameters.AddWithValue("$sessionId", sessionId);
        await using var reader = await promote.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var json = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.TryGetProperty("originalId", out var original) && root.TryGetProperty("promotedId", out var promoted)
                    && original.ValueKind == JsonValueKind.String && promoted.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(original.GetString()) && !string.IsNullOrWhiteSpace(promoted.GetString()))
                {
                    replacements[original.GetString()!] = promoted.GetString()!;
                }
            }
            catch (JsonException)
            {
                // Ignore malformed metadata; the id simply has no replacement.
            }
        }
    }

    // Materialize turns first so the reader is closed before we issue UPDATE commands.
    var turns = new List<(string TurnId, string? InputId, List<string> OutputIds)>();
    await using (var load = connection.CreateCommand())
    {
        load.Transaction = transaction;
        load.CommandText = """
            SELECT TurnId, InputInteractionId, OutputInteractionIdsJson
            FROM RolePlayV2Turns
            WHERE SessionId = $sessionId;
            """;
        load.Parameters.AddWithValue("$sessionId", sessionId);
        await using var reader = await load.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var outputJson = reader.GetString(2);
            List<string> outputs;
            try
            {
                outputs = JsonSerializer.Deserialize<List<string>>(outputJson) ?? [];
            }
            catch (JsonException)
            {
                outputs = [];
            }
            turns.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), outputs));
        }
    }

    var updated = 0;
    foreach (var (turnId, inputId, outputs) in turns)
    {
        string? newInput = inputId;
        var changed = false;
        if (!string.IsNullOrWhiteSpace(newInput))
        {
            if (replacements.TryGetValue(newInput, out var promotedInput))
            {
                newInput = promotedInput;
                changed = true;
            }
            else if (!liveIds.Contains(newInput))
            {
                newInput = null;
                changed = true;
            }
        }

        var newOutputs = new List<string>();
        foreach (var id in outputs)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (replacements.TryGetValue(id, out var promotedOutput))
            {
                if (!newOutputs.Contains(promotedOutput)) newOutputs.Add(promotedOutput);
                changed = true;
            }
            else if (liveIds.Contains(id))
            {
                newOutputs.Add(id);
            }
            else
            {
                changed = true; // stale reference -> drop
            }
        }

        if (!changed) continue;

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE RolePlayV2Turns
            SET InputInteractionId = $inputId,
                OutputInteractionIdsJson = $outputJson,
                OutputInteractionCount = $count,
                UpdatedUtc = $now
            WHERE SessionId = $sessionId AND TurnId = $turnId;
            """;
        update.Parameters.AddWithValue("$inputId", (object?)newInput ?? DBNull.Value);
        update.Parameters.AddWithValue("$outputJson", JsonSerializer.Serialize(newOutputs));
        update.Parameters.AddWithValue("$count", newOutputs.Count);
        update.Parameters.AddWithValue("$now", now);
        update.Parameters.AddWithValue("$sessionId", sessionId);
        update.Parameters.AddWithValue("$turnId", turnId);
        await update.ExecuteNonQueryAsync();
        updated++;
    }

    await transaction.CommitAsync();
    Console.WriteLine($"Turn membership reconciled for session '{sessionId}': {updated} turn(s) updated, {liveIds.Count} live interactions, {replacements.Count} replacement(s).");
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

    return await ExecuteSqlTextAsync(connection, sql, Path.GetFileName(sqlFile));
}

/// <summary>
/// Runs a single-statement .sql file's text. Read statements (SELECT/WITH/PRAGMA/EXPLAIN/VALUES)
/// print their result rows; write statements (UPDATE/INSERT/DELETE/REPLACE and DDL) execute
/// transactionally and report the number of rows affected.
/// </summary>
static async Task<int> ExecuteSqlTextAsync(SqliteConnection connection, string sql, string sourceName)
{
    if (IsReadStatement(sql))
        return await PrintQueryAsync(connection, sql);

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    try
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var rowsAffected = await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        Console.WriteLine($"OK: {sourceName} applied. Rows affected: {rowsAffected}");
        return 0;
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

/// <summary>Classifies a statement by its first keyword, skipping whitespace and SQL comments.</summary>
static bool IsReadStatement(string sql)
{
    var i = 0;
    while (i < sql.Length)
    {
        while (i < sql.Length && char.IsWhiteSpace(sql[i])) i++;
        if (i >= sql.Length) break;

        if (sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
        {
            while (i < sql.Length && sql[i] != '\n') i++;
            continue;
        }

        if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
        {
            i += 2;
            while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
            i = Math.Min(i + 2, sql.Length);
            continue;
        }

        var start = i;
        while (i < sql.Length && !char.IsWhiteSpace(sql[i]) && sql[i] != '(' && sql[i] != ';') i++;
        var token = sql[start..i].ToUpperInvariant();
        return token is "SELECT" or "WITH" or "PRAGMA" or "EXPLAIN" or "VALUES";
    }

    return true; // empty or comment-only — treat as a no-op read
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
    Console.Error.WriteLine("Commands: tables, schema [table], sessions, session <id>, adaptive <id>, themes <id>, evals <id>, transitions <id>, turns <id>, debug <id>, completions <id>, formula <id>, scenario <id>, gate-profiles, gate-rules <themeId>, theme-profiles, rp-themes <profileId>, provider-endpoint-update <providerId> <expectedCurrentBaseUrl> <newBaseUrl>, provider-split-model <sourceProviderId> <modelId> <newProviderName> <newBaseUrl>, provider-timeout-update <providerId> <expectedCurrentTimeoutSeconds> <newTimeoutSeconds>, b100-analyzer-configure, biglust-image-configure, qwen-edit-serverless-configure, qwen-edit-local-aio-configure, local-comfyui-configure <baseUrl>, set-identity-strength <modelIdentifier> <strength>, character-figure-update <scenarioId> <characterName> <weight> <bustSize> <buttSize>, api-image-configure, api-image-catalog, turn-membership-reconcile <sessionId>, b100-settle-plan <planId>, scene-asset-retag <assetId> <expectedCurrentType> <newType>, modelmanager-export [outFile], modelmanager-import <jsonFile>, sql <file> [id]");
}
