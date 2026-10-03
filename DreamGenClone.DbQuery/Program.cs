using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
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

// The identity re-key opens READ-WRITE only for its apply mode: 'preview' is guaranteed read-only at the
// connection level, not merely by convention.
var rekeyApplies = string.Equals(commandName, "b127-identity-rekey", StringComparison.Ordinal)
    && args.Skip(1).Any(argument => string.Equals(argument, "apply", StringComparison.OrdinalIgnoreCase));
var connectionMode = rekeyApplies || commandName is "provider-endpoint-update" or "provider-split-model" or "provider-timeout-update" or "provider-api-key-update" or "b100-analyzer-configure" or "b100-analyzer-openrouter-configure" or "biglust-image-configure" or "b137-krea2-configure" or "qwen21-lora-catalog-configure" or "qwen21-envelope-configure" or "image-editor-family-configure" or "qwen-edit-serverless-configure" or "qwen-edit-local-aio-configure" or "qwen-edit-local-aio-lora-configure" or "qwen-edit-remix-aio-configure" or "qwen-edit-remix-aio-lora-configure" or "api-image-configure" or "api-image-catalog" or "turn-membership-reconcile" or "b100-settle-plan" or "scene-asset-retag" or "set-identity-strength" or "character-figure-update" or "body-axes-migrate" or "local-comfyui-configure" or "modelmanager-import" or "sql" ? "ReadWrite" : "ReadOnly";
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
        "b137-krea2-configure" => await ConfigureKrea2Async(connection),
        "qwen21-lora-catalog-configure" => await ConfigureQwen21LoraCatalogAsync(connection),
        "image-editor-family-configure" => await ConfigureImageEditorFamilyAsync(
            connection,
            RequireArgument(args, 1, "modelIdentifier"),
            RequireArgument(args, 2, "sceneImageModelFamily"),
            RequireArgument(args, 3, "promptDialect")),
        "qwen21-envelope-configure" => await ConfigureQwen21EnvelopeAsync(
            connection,
            RequireArgument(args, 1, "steps"),
            RequireArgument(args, 2, "cfg"),
            RequireArgument(args, 3, "samplerName"),
            RequireArgument(args, 4, "scheduler")),
        "qwen-edit-serverless-configure" => await ConfigureQwenEditServerlessAsync(connection),
        "qwen-edit-local-aio-configure" => await ConfigureQwenEditLocalAioAsync(connection),
        "qwen-edit-local-aio-lora-configure" => await ConfigureQwenEditLocalAioLoraAsync(
            connection,
            RequireArgument(args, 1, "loraName"),
            RequireArgument(args, 2, "loraStrength")),
        "qwen-edit-remix-aio-configure" => await ConfigureQwenEditRemixAioAsync(connection),
        "qwen-edit-remix-aio-lora-configure" => await ConfigureQwenEditRemixAioLoraAsync(
            connection,
            RequireArgument(args, 1, "loraName"),
            RequireArgument(args, 2, "loraStrength")),
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
            RequireArgument(args, 3, "bustSize"),
            RequireArgument(args, 4, "buttSize")),
        // One-time: maps the retired BodyType onto the body axes that replaced it. Idempotent.
        "body-axes-migrate" => await MigrateBodyAxesAsync(connection),
        "modelmanager-export" => await ModelManagerTransfer.ExportAsync(
            connection,
            args.ElementAtOrDefault(1) ?? ModelManagerTransfer.DefaultExportPath),
        "modelmanager-import" => await ModelManagerTransfer.ImportAsync(
            connection,
            RequireArgument(args, 1, "jsonFile")),
        // B-127: identity ownership. 'preview' is read-only (the dispatcher opens the connection Mode=ReadOnly);
        // 'apply' renames the identity key columns and re-keys the rows in one transaction.
        "b127-identity-rekey" => await B127IdentityRekey.RunAsync(
            connection,
            RequireArgument(args, 1, "mode (preview|apply)"),
            args.Skip(2).ToList()),
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

// Converges the DeepSeek flash row onto the identifier the provider actually reports back.
// DeepSeek accepts `deepseek-v4-flash` as a REQUEST alias, but always reports the canonical
// `deepseek-flash` in the response `model` field. The structured-text client requires an exact match,
// so a row left on the alias fails every beat-pipeline call. Sanitized snapshots still carry the alias,
// so it is accepted as input here and rewritten to the canonical value (idempotent: a re-run is a no-op).
static async Task<int> ConfigureB100AnalyzerAsync(SqliteConnection connection)
{
    const string functionName = "RolePlaySceneBeatAnalyzer";
    const string providerName = "DeepSeek";
    const string modelIdentifier = "deepseek-flash";
    const string legacyModelIdentifier = "deepseek-v4-flash";

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
          AND rm.ModelIdentifier IN ($modelIdentifier, $legacyModelIdentifier)
          AND p.IsEnabled = 1
          AND rm.IsEnabled = 1;
        """;
    select.Parameters.AddWithValue("$providerName", providerName);
    select.Parameters.AddWithValue("$modelIdentifier", modelIdentifier);
    select.Parameters.AddWithValue("$legacyModelIdentifier", legacyModelIdentifier);

    var modelIds = new List<string>();
    await using (var reader = await select.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            modelIds.Add(reader.GetString(0));
    }

    if (modelIds.Count != 1)
    {
        throw new InvalidOperationException(
            $"Expected exactly one enabled '{providerName}' model '{modelIdentifier}' (or legacy '{legacyModelIdentifier}'), found {modelIds.Count}; no database changes were made.");
    }

    await using var configureModel = connection.CreateCommand();
    configureModel.Transaction = transaction;
    configureModel.CommandText = "UPDATE RegisteredModels SET ModelIdentifier = $modelIdentifier, StructuredOutputMode = 2 WHERE Id = $modelId;";
    configureModel.Parameters.AddWithValue("$modelIdentifier", modelIdentifier);
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
/// B-137: registers Krea 2 (Krea-2 Turbo) as a local ComfyUI scene-image generation model, declares the Character
/// LoRA identity it can carry, and seeds its scene-LoRA catalog.
///
/// The LoRA declaration is not cosmetic. A render surface offers the Character LoRA picker only when the selected
/// model's qualified strategies include Lora, so a model registered without it shows NO picker anywhere - which is
/// exactly how a trained Krea 2 character LoRA became unreachable on 2026-10-03. Both declaration fields are written
/// (visual for the render surfaces, identity for the production-media compilers) plus the qualification entry that
/// makes the declaration usable.
///
/// This is the portable form of the B-137 seed. The repo has no snapshot-refresh command
/// (<c>.github/instructions/db-snapshot-workflow.instructions.md</c>: "Share portable configuration as reviewed,
/// idempotent named commands in DreamGenClone.DbQuery, then run those commands on each host"), so this command -
/// not a copied .db - is how another machine gets Krea 2.
///
/// ADDITIVE ONLY. It never repoints a FunctionModelDefault, so nothing renders differently until an operator picks
/// the model. It also never repoints the local ComfyUI provider: it looks that provider up BY NAME and fails fast
/// if it is absent, because a Krea 2 model pointing at the wrong endpoint would fail at render time instead.
///
/// Deliberately NOT catalogued, and why:
///   * krea2_style_reference - the style-reference path was rejected (B-137 D1: text-only).
///   * krea2_filter_bypass3 - a 160-byte no-op proven useless in the proof matrix, and REMOVED from the host
///     on 2026-10-02 (moved to D:\ComfyUI\models\_dead_loras\) so it cannot be mistaken for a usable LoRA.
///   * krea2_nsfw_prompt_adherence - measured on 2026-10-02 and found to be a 268-byte empty stub, not a
///     usable LoRA; also removed from the host. It was previously excluded only as "never rendered".
///   * krea2_slider_detail / _realism / _weight - real weights staged on the host but never rendered, so no
///     strength was ever measured. They stay out of the menu rather than carrying a guessed strength;
///     a LoRA applied at a strength nobody chose is a different LoRA.
///
/// Idempotent: the model row is matched by model identifier and updated in place, and catalog rows are inserted
/// with ON CONFLICT DO NOTHING against UNIQUE (SceneImageModelFamily, FileName). An existing operator-edited row
/// is never overwritten.
/// </summary>
static async Task<int> ConfigureKrea2Async(SqliteConnection connection)
{
    const string providerName = "Local ComfyUI (WOOD-GAME-MAIN 5080)";
    const string modelIdentifier = "krea2_turbo_fp8_scaled.safetensors";
    const string modelDisplayName = "Krea 2 Turbo (Local ComfyUI)";
    const int familyKrea2 = 6;              // SceneImageModelFamily.Krea2
    const int dialectKrea2NaturalLanguage = 5; // SceneImagePromptDialect.Krea2NaturalLanguage

    var now = DateTime.UtcNow.ToString("o");
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

    string providerId;
    await using (var selectProvider = connection.CreateCommand())
    {
        selectProvider.Transaction = transaction;
        selectProvider.CommandText = "SELECT Id FROM Providers WHERE Name = $name;";
        selectProvider.Parameters.AddWithValue("$name", providerName);
        providerId = await selectProvider.ExecuteScalarAsync() as string
            ?? throw new InvalidOperationException(
                $"Provider '{providerName}' was not found, so there is nowhere to register the Krea 2 model. Run "
                + "'local-comfyui-configure <baseUrl>' first. No database changes were made.");
    }

    // Krea-2 Turbo is cfg-1 distilled and takes no negative text: the graph zeroes the positive conditioning
    // instead. The envelope is qualified here and is never read from the studio's SDXL-family controls.
    const string qualificationNote =
        "No reference conditioning, no edit path, no ControlNet. Krea-2 Turbo is cfg-1 "
        + "distilled, so the graph zeroes the positive conditioning instead of taking a negative prompt - the "
        + "sampler envelope is qualified here and is never read from the studio controls. Character LoRA identity "
        + "IS carried, through the entry below, and an artifact that declares no LoRA changes nothing here.";

    // LoRA identity. This entry is what makes the render's Character LoRA picker EXIST: ImageStepComposer and
    // SceneImageStudio ask the selected model's qualified strategies before they offer the control at all, so a
    // model without this entry shows no picker rather than an empty one. The Krea 2 graph re-points the sampler's
    // MODEL branch at a LoraLoaderModelOnly node and has no separate CLIP conditioning to re-point, which is the
    // same chain the seeded scene-LoRA catalog already renders through, so the capability is not new machinery.
    const string loraNote =
        "Krea-2 Turbo loads a LoRA through LoraLoaderModelOnly - the chain the scene-LoRA catalog already renders "
        + "through - and only the model branch is re-pointed, because this graph has no separate CLIP conditioning. "
        + "ProofId names the 2026-10-03 character-LoRA render on this model. No LoRA-free render changes: a LoRA is "
        + "loaded only when a render's picker actually selected one.";
    var qualifications =
        "[{\"Strategy\":\"TextToImage\",\"EndpointId\":\"" + providerId + "\",\"Qualified\":true,"
        + "\"ProofId\":\"krea2-59-cell-matrix-2026-10-01\","
        + "\"UnetName\":\"krea2_turbo_fp8_scaled.safetensors\","
        + "\"ClipName\":\"qwen3vl_4b_fp8_scaled.safetensors\","
        + "\"VaeName\":\"qwen_image_vae.safetensors\","
        + "\"Steps\":8,\"Cfg\":1.0,\"SamplerName\":\"euler\",\"Scheduler\":\"simple\",\"Denoise\":1.0,"
        + "\"Note\":\"" + qualificationNote.Replace("\"", "'") + "\"},"
        + "{\"Strategy\":\"Lora\",\"EndpointId\":\"" + providerId + "\",\"Qualified\":true,"
        + "\"ProofId\":\"krea2-turbo-becky-lora-2026-10-03\","
        + "\"Note\":\"" + loraNote.Replace("\"", "'") + "\"}]";

    // TWO different declarations, and the difference is load-bearing. The render surfaces' LoRA picker asks
    // ReferenceStrategyResolver.ListAvailableStrategies, which reads SupportedVisualStrategiesJson; the
    // production-media compilers read SupportedIdentityStrategiesJson. Declaring Lora in only one of them would
    // leave the picker absent on exactly the surfaces the operator uses.
    const string supportedIdentityStrategies = "[\"Lora\"]";
    const string supportedVisualStrategies = "[\"TextOnly\",\"Lora\"]";

    const string modelNotes =
        "Krea-2 Turbo 12B DiT + Qwen3-VL 4B text encoder + Qwen Image VAE, local ComfyUI only. Text-to-image with "
        + "no reference conditioning, plus Character LoRA identity (LoraLoaderModelOnly). Proof: 59-cell matrix in "
        + "helpers/local-comfyui-host/run-krea2-proof.ps1 (specs/Planning/B-137-krea2-local-generation).";

    string modelId;
    await using (var selectModel = connection.CreateCommand())
    {
        selectModel.Transaction = transaction;
        selectModel.CommandText = "SELECT Id FROM RegisteredModels WHERE ModelIdentifier = $modelIdentifier;";
        selectModel.Parameters.AddWithValue("$modelIdentifier", modelIdentifier);
        var existingModelId = await selectModel.ExecuteScalarAsync();
        if (existingModelId is string foundModelId)
        {
            modelId = foundModelId;
            await using var updateModel = connection.CreateCommand();
            updateModel.Transaction = transaction;
            updateModel.CommandText = """
                UPDATE RegisteredModels
                SET ProviderId = $providerId,
                    DisplayName = $displayName,
                    ModelKind = 1,
                    SceneImageModelFamily = $family,
                    PromptDialect = $dialect,
                    SupportedIdentityStrategiesJson = $identityStrategies,
                    SupportedVisualStrategiesJson = $visualStrategies,
                    CapabilityQualificationsJson = $qualifications,
                    Notes = $notes,
                    IsEnabled = 1
                WHERE Id = $modelId;
                """;
            updateModel.Parameters.AddWithValue("$providerId", providerId);
            updateModel.Parameters.AddWithValue("$displayName", modelDisplayName);
            updateModel.Parameters.AddWithValue("$family", familyKrea2);
            updateModel.Parameters.AddWithValue("$dialect", dialectKrea2NaturalLanguage);
            updateModel.Parameters.AddWithValue("$identityStrategies", supportedIdentityStrategies);
            updateModel.Parameters.AddWithValue("$visualStrategies", supportedVisualStrategies);
            updateModel.Parameters.AddWithValue("$qualifications", qualifications);
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
                    ModelKind, SupportsImageInput, SceneImageModelFamily, PromptDialect,
                    SupportsStructuredJsonSchema, StructuredOutputMode,
                    SupportedIdentityStrategiesJson, SupportedVisualStrategiesJson,
                    CapabilityQualificationsJson, IsDefault)
                VALUES (
                    $id, $providerId, $modelIdentifier, $displayName, 1, $now,
                    0, 'fp8', '12B', $notes, 0,
                    1, 0, $family, $dialect,
                    0, 0,
                    $identityStrategies, $visualStrategies,
                    $qualifications, 0);
                """;
            insertModel.Parameters.AddWithValue("$id", modelId);
            insertModel.Parameters.AddWithValue("$providerId", providerId);
            insertModel.Parameters.AddWithValue("$modelIdentifier", modelIdentifier);
            insertModel.Parameters.AddWithValue("$displayName", modelDisplayName);
            insertModel.Parameters.AddWithValue("$family", familyKrea2);
            insertModel.Parameters.AddWithValue("$dialect", dialectKrea2NaturalLanguage);
            insertModel.Parameters.AddWithValue("$identityStrategies", supportedIdentityStrategies);
            insertModel.Parameters.AddWithValue("$visualStrategies", supportedVisualStrategies);
            insertModel.Parameters.AddWithValue("$qualifications", qualifications);
            insertModel.Parameters.AddWithValue("$notes", modelNotes);
            insertModel.Parameters.AddWithValue("$now", now);
            await insertModel.ExecuteNonQueryAsync();
        }
    }

    // The catalog table itself. Byte-identical to SceneLoraRepository.SchemaSql, and the app also ensures it at
    // startup; it is created here so this command works standalone on a fresh sanitized snapshot.
    await using (var createCatalog = connection.CreateCommand())
    {
        createCatalog.Transaction = transaction;
        createCatalog.CommandText = """
            CREATE TABLE IF NOT EXISTS SceneLoras (
                Id TEXT PRIMARY KEY,
                FileName TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                SceneImageModelFamily TEXT NOT NULL,
                Category TEXT NOT NULL,
                DefaultStrength REAL NOT NULL CHECK (DefaultStrength > 0),
                IsEnabled INTEGER NOT NULL CHECK (IsEnabled IN (0, 1)),
                Notes TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UNIQUE (SceneImageModelFamily, FileName)
            );
            """;
        await createCatalog.ExecuteNonQueryAsync();
    }

    await using (var createIndex = connection.CreateCommand())
    {
        createIndex.Transaction = transaction;
        createIndex.CommandText = """
            CREATE INDEX IF NOT EXISTS IX_SceneLoras_Family
                ON SceneLoras (SceneImageModelFamily, IsEnabled, Category);
            """;
        await createIndex.ExecuteNonQueryAsync();
    }

    // Every DefaultStrength below is the strength that was ACTUALLY rendered in the proof matrix.
    (string Id, string FileName, string DisplayName, string Category, double Strength, string Notes)[] loras =
    [
        ("krea2-lora-nsfw-v4", "krea2_nsfw_v4_v43exp.safetensors", "Krea2 NSFW V4", "Unlock", 1.0,
            "Flagship unpaker. Cell 1: full frontal nudity on base weights + stock TE. Every explicit-act cell chains it at 1.0."),
        ("krea2-lora-mystic-v3", "krea2_mysticxxx_v3.safetensors", "Krea2 Mystic XXX v3", "Unlock", 0.8,
            "Cell 5 (nudity-mystic-0.8): nudes without the V4 stack. Rendered at 0.8."),
        ("krea2-lora-act-deepthroat", "krea2_act_deepthroat_v2.safetensors", "Deepthroat (act)", "Act", 0.8,
            "Cells 6 / 40: fellatio-class act. On BASE weights the act needs this LoKr - base+V4 alone renders an embrace."),
        ("krea2-lora-act-cowgirl", "krea2_act_Cowgirl-POV-v1-step0800.safetensors", "Cowgirl POV (act LoKr)", "Act", 1.0,
            "Cell 58 (lokr-cowgirl): explicit penetration rendered with V4 at 1.0 + this LoKr at 1.0."),
        ("krea2-lora-act-missionary", "krea2_act_Missionary-POV-v1-step0700.safetensors", "Missionary POV (act LoKr)", "Act", 1.0,
            "Cell 56 (lokr-missionary): explicit penetration rendered with V4 at 1.0 + this LoKr at 1.0."),
        ("krea2-lora-act-rearentry", "krea2_act_RearEntry-POV-v1-step0800.safetensors", "Rear entry POV (act LoKr)", "Act", 1.0,
            "Cell 57 (lokr-rearentry): explicit penetration rendered with V4 at 1.0 + this LoKr at 1.0."),
        ("krea2-lora-act-lyingoral", "krea2_act_Lying-Oral-POV-v1-step0700.safetensors", "Lying oral POV (act LoKr)", "Act", 1.0,
            "Cell 55 (lokr-lying-oral): explicit oral rendered with V4 at 1.0 + this LoKr at 1.0."),
        ("krea2-lora-act-matingpress", "krea2_act_Mating-Press-v1-step0700.safetensors", "Mating press (act LoKr)", "Act", 1.0,
            "Grounded per-act LoKr from the same training run as the other four. Strength 1.0 matches its siblings. Not individually cell-proven - verify before relying on it."),
        ("krea2-lora-anatomy-pussyhm", "krea2_anatomy_pussyhm.safetensors", "Female anatomy helper", "Anatomy", 0.8,
            "Cell 8 (female-anatomy): chained after V4 at 1.0, this at 0.8."),
        ("krea2-lora-anatomy-dicktator", "krea2_anatomy_dicktator_male.safetensors", "Male anatomy helper", "Anatomy", 0.8,
            "Cells 7 / 20: chained after V4 at 1.0 (or alone on the uncensored checkpoint), this at 0.8."),
        ("krea2-lora-anatomy-breastshm", "krea2_anatomy_breastshm.safetensors", "Breast helper", "Anatomy", 0.8,
            "Sibling of the pussyhm helper from the same author, seeded at the same proven strength (0.8). Not individually cell-proven."),
        ("krea2-lora-ultrarealism", "krea2_bloomgirls_ultrarealism.safetensors", "Bloomgirls ultrarealism", "Style", 0.6,
            "Cell 9 (stacked-realism): V4 at 1.0 + this at 0.6."),
    ];

    var catalogInserted = 0;
    foreach (var lora in loras)
    {
        await using var insertLora = connection.CreateCommand();
        insertLora.Transaction = transaction;
        insertLora.CommandText = """
            INSERT INTO SceneLoras
                (Id, FileName, DisplayName, SceneImageModelFamily, Category, DefaultStrength, IsEnabled, Notes, CreatedUtc)
            VALUES ($id, $fileName, $displayName, 'Krea2', $category, $strength, 1, $notes, $now)
            ON CONFLICT (SceneImageModelFamily, FileName) DO NOTHING;
            """;
        insertLora.Parameters.AddWithValue("$id", lora.Id);
        insertLora.Parameters.AddWithValue("$fileName", lora.FileName);
        insertLora.Parameters.AddWithValue("$displayName", lora.DisplayName);
        insertLora.Parameters.AddWithValue("$category", lora.Category);
        insertLora.Parameters.AddWithValue("$strength", lora.Strength);
        insertLora.Parameters.AddWithValue("$notes", lora.Notes);
        insertLora.Parameters.AddWithValue("$now", now);
        catalogInserted += await insertLora.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
    Console.WriteLine(
        $"Krea 2 configured: {providerName} | {modelIdentifier} (Krea2 / Krea2NaturalLanguage, TextOnly + Lora) | "
        + $"catalog rows inserted {catalogInserted}/{loras.Length} (existing rows left as they were)");
    Console.WriteLine(
        "Additive only: no function default was changed, so nothing renders differently until Krea 2 is picked.");
    return 0;
}

/// <summary>
/// Sets an IMAGE EDITOR model row's scene-image FAMILY and prompt DIALECT together.
///
/// Why both at once: the two are validated as a PAIR by <see cref="SceneImagePromptMetadata.IsCompatible"/>, and
/// every other pair (including family-set/dialect-unknown) is refused. A scene-LoRA selection is checked against the
/// row's family, so an editor row left at 'Unknown' refuses every selection instead of applying a LoRA trained for a
/// different checkpoint - which is why an editor whose graph kind already says Qwen-Image-2.1 still needs this.
///
/// The compatible pairs come from that single owner; this command never re-states them, and it fails fast when the
/// identifier does not resolve to exactly one editor row.
/// </summary>
static async Task<int> ConfigureImageEditorFamilyAsync(
    SqliteConnection connection,
    string modelIdentifier,
    string familyArgument,
    string dialectArgument)
{
    if (!Enum.TryParse<SceneImageModelFamily>(familyArgument, ignoreCase: true, out var family))
    {
        throw new ArgumentException(
            $"'{familyArgument}' is not a scene-image family. Use one of: "
            + $"{string.Join(", ", Enum.GetNames<SceneImageModelFamily>())}; no database changes were made.");
    }

    if (!Enum.TryParse<SceneImagePromptDialect>(dialectArgument, ignoreCase: true, out var dialect))
    {
        throw new ArgumentException(
            $"'{dialectArgument}' is not a scene-image prompt dialect. Use one of: "
            + $"{string.Join(", ", Enum.GetNames<SceneImagePromptDialect>())}; no database changes were made.");
    }

    if (!SceneImagePromptMetadata.IsCompatible(family, dialect))
    {
        throw new ArgumentException(
            $"{family} is not compatible with {dialect}: the compatible pairs live in SceneImagePromptMetadata, so "
            + "this pair would make the row unsaveable in Model Manager; no database changes were made.");
    }

    var rows = new List<(string Id, string DisplayName, int Family, int Dialect)>();
    await using (var select = connection.CreateCommand())
    {
        select.CommandText =
            """
            SELECT Id, DisplayName, COALESCE(SceneImageModelFamily, 0), COALESCE(PromptDialect, 0)
            FROM RegisteredModels
            WHERE ModelIdentifier = $modelIdentifier AND ImageEditorGraphKind IS NOT NULL;
            """;
        select.Parameters.AddWithValue("$modelIdentifier", modelIdentifier.Trim());
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
        }
    }

    if (rows.Count != 1)
    {
        throw new InvalidOperationException(
            $"Expected exactly one image-editor row with model identifier '{modelIdentifier}', found {rows.Count}. "
            + "Name the editor row's identifier exactly; no database changes were made.");
    }

    var row = rows[0];
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    await using (var update = connection.CreateCommand())
    {
        update.Transaction = transaction;
        update.CommandText =
            """
            UPDATE RegisteredModels
            SET SceneImageModelFamily = $family, PromptDialect = $dialect
            WHERE Id = $id;
            """;
        update.Parameters.AddWithValue("$family", (int)family);
        update.Parameters.AddWithValue("$dialect", (int)dialect);
        update.Parameters.AddWithValue("$id", row.Id);
        if (await update.ExecuteNonQueryAsync() != 1)
        {
            throw new InvalidOperationException($"Expected exactly one row to change for '{row.DisplayName}'.");
        }
    }

    await transaction.CommitAsync();
    Console.WriteLine(
        $"{row.DisplayName}: family {row.Family} -> {(int)family} ({family}), "
        + $"dialect {row.Dialect} -> {(int)dialect} ({dialect})");
    Console.WriteLine(
        "A scene-LoRA selection is now validated against this family instead of being refused as Unknown.");
    return 0;
}

/// <summary>
/// Rewrites the sampler envelope inside a Qwen-Image-2.1 model row's qualified capability record: steps, cfg,
/// sampler and scheduler. Every value is an explicit argument - none of them is defaulted or guessed here.
///
/// Why this exists rather than an ad-hoc UPDATE: the envelope is the part of the qualification that the NSFW LoRA
/// ecosystem says must change. The main 2.1 LoRA's author states euler "gives very bad results" and recommends
/// 25+ steps, cfg 3-6, er_sde and beta; at cfg 1.0 the negative branch is also inert. The shipped row carried
/// 25 / cfg 1.0 / euler / simple, and every 2.1 render in the token-aware proof programme that produced usable
/// structure ran cfg 3.0 with er_sde/beta.
///
/// The artifacts (UnetName / TextEncoderName / VaeName), the reference ceiling and their notes are PRESERVED -
/// only the four sampler values and a provenance note change. Fails fast when the family does not resolve to
/// exactly one enabled model row, so it can never quietly patch the wrong thing.
/// </summary>
static async Task<int> ConfigureQwen21EnvelopeAsync(
    SqliteConnection connection,
    string stepsArg,
    string cfgArg,
    string samplerName,
    string scheduler)
{
    if (!int.TryParse(stepsArg, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var steps) || steps <= 0)
        throw new ArgumentException($"Steps '{stepsArg}' must be a positive integer; no database changes were made.");
    if (!double.TryParse(cfgArg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cfg) || cfg <= 0)
        throw new ArgumentException($"Cfg '{cfgArg}' must be a positive number; no database changes were made.");
    if (string.IsNullOrWhiteSpace(samplerName))
        throw new ArgumentException("Sampler name is required; no database changes were made.");
    if (string.IsNullOrWhiteSpace(scheduler))
        throw new ArgumentException("Scheduler is required; no database changes were made.");

    const int qwenImage21Family = 5;

    var rows = new List<(string Id, string DisplayName, string? Qualifications)>();
    await using (var select = connection.CreateCommand())
    {
        select.CommandText =
            """
            SELECT Id, DisplayName, CapabilityQualificationsJson
            FROM RegisteredModels
            WHERE SceneImageModelFamily = $family AND IsEnabled = 1
            ORDER BY IsDefault DESC, DisplayName;
            """;
        select.Parameters.AddWithValue("$family", qwenImage21Family);
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
    }

    if (rows.Count != 1)
    {
        throw new InvalidOperationException(
            $"Expected exactly one enabled Qwen-Image-2.1 model row, found {rows.Count}. Repoint the model rows in "
            + "Model Manager first; no database changes were made.");
    }

    var row = rows[0];
    if (string.IsNullOrWhiteSpace(row.Qualifications))
    {
        throw new InvalidOperationException(
            $"Model '{row.DisplayName}' has no capability qualifications to amend; no database changes were made.");
    }

    var qualifications = JsonNode.Parse(row.Qualifications) as JsonArray
        ?? throw new InvalidOperationException(
            $"Model '{row.DisplayName}' has malformed qualifications; no database changes were made.");

    var qualified = qualifications
        .OfType<JsonObject>()
        .FirstOrDefault(entry => entry["Qualified"]?.GetValue<bool>() == true)
        ?? throw new InvalidOperationException(
            $"Model '{row.DisplayName}' has no qualified entry to amend; no database changes were made.");

    var previous = $"{qualified["Steps"]?.GetValue<int>()} steps, cfg {qualified["Cfg"]?.GetValue<double>()}, "
        + $"{qualified["SamplerName"]?.GetValue<string>()}/{qualified["Scheduler"]?.GetValue<string>()}";

    qualified["Steps"] = steps;
    qualified["Cfg"] = cfg;
    qualified["SamplerName"] = samplerName;
    qualified["Scheduler"] = scheduler;
    qualified["EnvelopeNote"] =
        "Envelope re-qualified 2026-10-02 on operator evidence: the shipped 25 / cfg 1.0 / euler / simple envelope "
        + "rendered mangled bodies on the 49-cell baseline, and cfg 1.0 leaves the negative branch inert. The main "
        + "Qwen-Image-2.1 NSFW LoRA's author states euler explicitly fails for him and recommends 25+ steps, cfg "
        + "3-6, er_sde and beta; every arm of specs/image-generator-tests/qwen-21-explicit-anatomy that produced "
        + "coherent two-body anatomy ran cfg 3.0 with er_sde/beta.";

    var updated = qualifications.ToJsonString();

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    await using (var update = connection.CreateCommand())
    {
        update.Transaction = transaction;
        update.CommandText =
            "UPDATE RegisteredModels SET CapabilityQualificationsJson = $qualifications WHERE Id = $id;";
        update.Parameters.AddWithValue("$qualifications", updated);
        update.Parameters.AddWithValue("$id", row.Id);
        if (await update.ExecuteNonQueryAsync() != 1)
        {
            throw new InvalidOperationException($"Expected exactly one row to change for '{row.DisplayName}'.");
        }
    }

    await transaction.CommitAsync();
    Console.WriteLine($"{row.DisplayName}: envelope {previous} -> {steps} steps, cfg {cfg}, {samplerName}/{scheduler}");
    Console.WriteLine("Artifacts, reference ceiling and their notes were preserved.");
    return 0;
}

/// <summary>
/// Seeds the scene-LoRA catalog with the Qwen-Image-2.1 explicit adapters that are installed on the local
/// ComfyUI host, so the studio's family-filtered picker can offer them for a Qwen-Image-2.1 model.
///
/// Additive and idempotent: it inserts catalog rows only (ON CONFLICT DO NOTHING), never repoints a function
/// default, never touches a model row and never disables anything. Until a render selects one of these rows the
/// graph emits no loader node at all, so an existing pipeline is unchanged.
///
/// Provenance: every strength recorded here is a strength that was ACTUALLY rendered in the qwen-21 explicit
/// anatomy proof programme (specs/image-generator-tests/qwen-21-explicit-anatomy/). The v1 vulva specialist is
/// inserted DISABLED because it regressed against the base model - the row is kept so its provenance and the
/// reason are not lost.
/// </summary>
static async Task<int> ConfigureQwen21LoraCatalogAsync(SqliteConnection connection)
{
    var now = DateTime.UtcNow.ToString("o");
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

    // The catalog table itself. Byte-identical to SceneLoraRepository.SchemaSql, and the app also ensures it at
    // startup; it is created here so this command works standalone on a fresh sanitized snapshot.
    await using (var createCatalog = connection.CreateCommand())
    {
        createCatalog.Transaction = transaction;
        createCatalog.CommandText = """
            CREATE TABLE IF NOT EXISTS SceneLoras (
                Id TEXT PRIMARY KEY,
                FileName TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                SceneImageModelFamily TEXT NOT NULL,
                Category TEXT NOT NULL,
                DefaultStrength REAL NOT NULL CHECK (DefaultStrength > 0),
                IsEnabled INTEGER NOT NULL CHECK (IsEnabled IN (0, 1)),
                Notes TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UNIQUE (SceneImageModelFamily, FileName)
            );
            """;
        await createCatalog.ExecuteNonQueryAsync();
    }

    await using (var createIndex = connection.CreateCommand())
    {
        createIndex.Transaction = transaction;
        createIndex.CommandText = """
            CREATE INDEX IF NOT EXISTS IX_SceneLoras_Family
                ON SceneLoras (SceneImageModelFamily, IsEnabled, Category);
            """;
        await createIndex.ExecuteNonQueryAsync();
    }

    (string Id, string FileName, string DisplayName, string Category, double Strength, bool Enabled, string Notes)[] loras =
    [
        ("qwen21-lora-thesealpacas-v2", "NSFW Qwen by TheseAlpacas V2.safetensors", "TheseAlpacas NSFW Qwen v2 (main)", "Unlock", 1.0, true,
            "The main 2.1 explicit adapter: most-downloaded of any (21.6k) and the one both its author and the "
            + "penetration LoRA's author say to pair with everything else. Rendered at 1.0 in the s10 size sweep and "
            + "the s11 penetration suite and at 0.6 in s11/s12. Author's recipe: 25+ steps, cfg 3-6, strength 0.8-1.0, "
            + "er_sde/beta, and euler explicitly fails for him. Known gap: vulva fidelity without penetration."),
        ("qwen21-lora-penetration-v5", "translucent_penetration-V5+Qwen-Image-2.1.safetensors", "Translucent Penetration v5 (2.1)", "Act", 1.0, true,
            "The only 2.1 penetration specialist that exists (4.3k dl). Rendered at 1.0 in s11 and at 0.6 in s12. It "
            + "made the join READABLE (glans no longer visible, labia around the shaft) where the base model rendered "
            + "the penis lying alongside - the single biggest structural gain of the programme - but it stretches the "
            + "labia and does not reduce size. Author's constraints: 1.0-2.5 MP canvas (2048^2 = 4.2 MP is outside its "
            + "trained range), cfg ~3.0, strength 1.0, and it expects a general NSFW LoRA beside it."),
        ("qwen21-lora-penis-v01", "qwen2.1_penisV01_000004956.safetensors", "Perfect erect penis (2.1)", "Anatomy", 0.8, true,
            "Male detail specialist, rendered alone at 0.8 in s11. Renders the largest, most veined and most detailed "
            + "penis of every arm tested: it is a DETAIL specialist, not a proportion corrector. Pair with the size "
            + "control below when normal proportions matter."),
        ("qwen21-lora-penis-coachbate", "qwen-image-2.1_penis_coachbate_preview1.safetensors", "CoachBate penis (2.1)", "Anatomy", 1.0, true,
            "Male anatomy specialist rendered at 1.0 across the malestate, maleneg, multiperson and genital suites. "
            + "Recurring defect observed across arms: a notched / cleft glans."),
        ("qwen21-lora-vagina-v2", "pussyV2.safetensors", "qwen 2.1 vagina v2.0", "Anatomy", 0.6, true,
            "Female specialist v2.0 (published 2026-10-02). Rendered at 0.6 in s11/s12. It did not visibly repair the "
            + "labial distortion the penetration LoRA introduces, so it is a candidate for a higher strength rather "
            + "than a proven fix."),
        ("qwen21-lora-penis-small", "Q21 make the penis small.safetensors", "Penis size control (small)", "Style", 0.6, true,
            "The only size-control adapter that exists for 2.1. Rendered at 0.6 and 1.0 in s12 and it DOES reduce the "
            + "drawn size, so size is steerable - the fix for the operator's 'abnormal penis size' rejection of the "
            + "s11 arms. Categorised Style because it steers proportion rather than anatomical fidelity."),
        ("qwen21-lora-vagina-v1", "qwen21_v2_000002750.safetensors", "qwen 2.1 vagina v1.0 (regressed)", "Anatomy", 1.0, false,
            "DISABLED - kept for provenance. At 1.0 it stripped pubic hair and body texture and softened the vulva; "
            + "the base model was equal or better in all four distance rows of the s7 female suite. Superseded by v2.0."),
    ];

    var inserted = 0;
    foreach (var lora in loras)
    {
        await using var insertLora = connection.CreateCommand();
        insertLora.Transaction = transaction;
        insertLora.CommandText = """
            INSERT INTO SceneLoras
                (Id, FileName, DisplayName, SceneImageModelFamily, Category, DefaultStrength, IsEnabled, Notes, CreatedUtc)
            VALUES ($id, $fileName, $displayName, 'QwenImage21', $category, $strength, $enabled, $notes, $now)
            ON CONFLICT (SceneImageModelFamily, FileName) DO NOTHING;
            """;
        insertLora.Parameters.AddWithValue("$id", lora.Id);
        insertLora.Parameters.AddWithValue("$fileName", lora.FileName);
        insertLora.Parameters.AddWithValue("$displayName", lora.DisplayName);
        insertLora.Parameters.AddWithValue("$category", lora.Category);
        insertLora.Parameters.AddWithValue("$strength", lora.Strength);
        insertLora.Parameters.AddWithValue("$enabled", lora.Enabled ? 1 : 0);
        insertLora.Parameters.AddWithValue("$notes", lora.Notes);
        insertLora.Parameters.AddWithValue("$now", now);
        inserted += await insertLora.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
    var enabled = loras.Count(l => l.Enabled);
    Console.WriteLine(
        $"Qwen-Image-2.1 scene-LoRA catalog: inserted {inserted}/{loras.Length} rows ({enabled} enabled, "
        + $"{loras.Length - enabled} disabled), existing rows left as they were.");
    Console.WriteLine(
        "Additive only: no model row and no function default was changed, so nothing renders differently until a "
        + "Qwen-Image-2.1 render selects one of these LoRAs.");
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

/// <summary>
/// Registers the local Qwen editor variant that layers the Gay/Trans editor LoRA onto the v23 AIO
/// checkpoint. Operator-accepted strengths: 0.8 (best) and 1.0. Rejected strengths are not defaulted.
/// </summary>
static Task<int> ConfigureQwenEditLocalAioLoraAsync(
    SqliteConnection connection,
    string loraName,
    string loraStrengthArg)
{
    if (string.IsNullOrWhiteSpace(loraName))
        throw new InvalidOperationException("An editor LoRA artifact name is required. No database changes were made.");

    if (!double.TryParse(loraStrengthArg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var loraStrength) || loraStrength <= 0)
        throw new InvalidOperationException($"Editor LoRA strength '{loraStrengthArg}' must be an explicit positive number; no default is applied. No database changes were made.");

    return CloneLocalEditorVariantAsync(
        connection,
        "Qwen-Rapid-AIO-NSFW-v23.safetensors",
        "qwen_edit_local_aio_v23_gaylora",
        "Qwen Image Edit Rapid-AIO NSFW v23 + Gay/Trans LoRA (Local ComfyUI)",
        loraName,
        loraStrength);
}

/// <summary>
/// Registers the Remix AIO v2.0 merged checkpoint as a separate local editor variant (no LoRA).
/// Verified 2026-09-12 as a drop-in for the MergedCheckpoint graph (baked CLIP+VAE, single
/// CheckpointLoaderSimple); anatomy SHAPE was correct though the LoRA variant was preferred overall.
/// </summary>
static Task<int> ConfigureQwenEditRemixAioAsync(SqliteConnection connection) =>
    CloneLocalEditorVariantAsync(
        connection,
        "qwenImageEditRemix_aioV20.safetensors",
        "qwen_edit_local_remix_aio_v20",
        "Qwen Image Edit Remix AIO v2.0 (Local ComfyUI)",
        null,
        null);

/// <summary>
/// Registers the local Qwen editor variant that layers the Gay/Trans editor LoRA onto the REMIX AIO
/// v2.0 merged checkpoint. Same clone path as the v23 LoRA variant; operator-accepted strength 0.8.
/// </summary>
static Task<int> ConfigureQwenEditRemixAioLoraAsync(
    SqliteConnection connection,
    string loraName,
    string loraStrengthArg)
{
    if (string.IsNullOrWhiteSpace(loraName))
        throw new InvalidOperationException("An editor LoRA artifact name is required. No database changes were made.");

    if (!double.TryParse(loraStrengthArg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var loraStrength) || loraStrength <= 0)
        throw new InvalidOperationException($"Editor LoRA strength '{loraStrengthArg}' must be an explicit positive number; no default is applied. No database changes were made.");

    return CloneLocalEditorVariantAsync(
        connection,
        "qwenImageEditRemix_aioV20.safetensors",
        "qwen_edit_local_remix_aio_v20_lora",
        "Qwen Image Edit Remix AIO v2.0 + Gay/Trans LoRA (Local ComfyUI)",
        loraName,
        loraStrength);
}

/// <summary>
/// Creates (or refreshes) a local Qwen editor model VARIANT by CLONING the existing local editor row,
/// so every unrelated column stays identical, and overriding only identity plus the checkpoint and the
/// optional editor LoRA pair. Idempotent by ModelIdentifier: re-running updates that row in place rather
/// than creating duplicates. The source row is never modified, no strength is defaulted, and a LoRA name
/// without a strength (or a strength without a name) fails fast.
/// </summary>
static async Task<int> CloneLocalEditorVariantAsync(
    SqliteConnection connection,
    string checkpoint,
    string newIdentifier,
    string displayName,
    string? loraName,
    double? loraStrength)
{
    const string providerName = "Local ComfyUI (WOOD-GAME-MAIN 5080)";
    // Matched on the EDITOR CHECKPOINT, not ModelIdentifier: the registered local editor row keeps the
    // generic qwen_image_edit_2511_fp8mixed identifier and carries the AIO checkpoint name in
    // ImageEditorDiffusionModel. The new row needs a distinct ModelIdentifier because the table is
    // UNIQUE on (ProviderId, ModelIdentifier).
    const string sourceDiffusionModel = "Qwen-Rapid-AIO-NSFW-v23.safetensors";

    var hasLoraName = !string.IsNullOrWhiteSpace(loraName);
    if (hasLoraName != (loraStrength is not null))
        throw new InvalidOperationException("An editor LoRA requires BOTH a name and an explicit strength, or neither. No database changes were made.");

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

    string sourceId;
    string providerId;
    await using (var selectSource = connection.CreateCommand())
    {
        selectSource.Transaction = transaction;
        // The PLAIN row is the variant template, so require ImageEditorLoraName IS NULL: once a LoRA
        // variant exists, two rows can carry the same checkpoint and the source pick would otherwise be
        // whichever row the engine happened to return first.
        selectSource.CommandText = "SELECT Id, ProviderId FROM RegisteredModels WHERE ImageEditorDiffusionModel = $diffusionModel AND ImageEditorLoraName IS NULL AND ProviderId = (SELECT Id FROM Providers WHERE Name = $provider);";
        selectSource.Parameters.AddWithValue("$diffusionModel", sourceDiffusionModel);
        selectSource.Parameters.AddWithValue("$provider", providerName);
        await using var reader = await selectSource.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"No local editor model using checkpoint '{sourceDiffusionModel}' was found for provider '{providerName}'. No database changes were made.");
        sourceId = reader.GetString(0);
        providerId = reader.GetString(1);
    }

    string? existingId = null;
    await using (var selectExisting = connection.CreateCommand())
    {
        selectExisting.Transaction = transaction;
        selectExisting.CommandText = "SELECT Id FROM RegisteredModels WHERE ModelIdentifier = $identifier AND ProviderId = $providerId;";
        selectExisting.Parameters.AddWithValue("$identifier", newIdentifier);
        selectExisting.Parameters.AddWithValue("$providerId", providerId);
        existingId = await selectExisting.ExecuteScalarAsync() as string;
    }

    // Clone every column from the source row so future schema additions stay in sync automatically;
    // only the identity and LoRA columns are substituted.
    var columns = new List<string>();
    await using (var pragma = connection.CreateCommand())
    {
        pragma.Transaction = transaction;
        pragma.CommandText = "SELECT name FROM pragma_table_info('RegisteredModels');";
        await using var reader = await pragma.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
    }

    var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Id"] = "$newId",
        ["ModelIdentifier"] = "$newIdentifier",
        ["DisplayName"] = "$displayName",
        ["IsEnabled"] = "1",
        ["ImageEditorDiffusionModel"] = "$checkpoint",
        ["ImageEditorLoraName"] = "$loraName",
        ["ImageEditorLoraStrength"] = "$loraStrength"
    };

    var insertColumns = string.Join(", ", columns);
    var selectExpressions = string.Join(", ", columns.Select(column => overrides.TryGetValue(column, out var parameter) ? parameter : column));

    string outcome;
    if (existingId is null)
    {
        var newId = Guid.NewGuid().ToString();
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO RegisteredModels ({insertColumns}) SELECT {selectExpressions} FROM RegisteredModels WHERE Id = $sourceId;";
        insert.Parameters.AddWithValue("$newId", newId);
        insert.Parameters.AddWithValue("$newIdentifier", newIdentifier);
        insert.Parameters.AddWithValue("$displayName", displayName);
        insert.Parameters.AddWithValue("$checkpoint", checkpoint);
        insert.Parameters.AddWithValue("$loraName", (object?)loraName ?? DBNull.Value);
        insert.Parameters.AddWithValue("$loraStrength", (object?)loraStrength ?? DBNull.Value);
        insert.Parameters.AddWithValue("$sourceId", sourceId);
        if (await insert.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("Editor variant model insert failed; no database changes were made.");
        outcome = $"Created editor variant model {newId} (cloned from {sourceId})";
    }
    else
    {
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE RegisteredModels SET DisplayName = $displayName, IsEnabled = 1, ImageEditorDiffusionModel = $checkpoint, ImageEditorLoraName = $loraName, ImageEditorLoraStrength = $loraStrength WHERE Id = $existingId;";
        update.Parameters.AddWithValue("$displayName", displayName);
        update.Parameters.AddWithValue("$checkpoint", checkpoint);
        update.Parameters.AddWithValue("$loraName", (object?)loraName ?? DBNull.Value);
        update.Parameters.AddWithValue("$loraStrength", (object?)loraStrength ?? DBNull.Value);
        update.Parameters.AddWithValue("$existingId", existingId);
        if (await update.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("Editor variant model update failed; no database changes were made.");
        outcome = $"Updated editor variant model {existingId}";
    }

    await transaction.CommitAsync();
    Console.WriteLine(
        $"{outcome}: {displayName} | checkpoint={checkpoint} | LoRA={(hasLoraName ? $"{loraName} @ {loraStrength}" : "(none)")} | ModelIdentifier={newIdentifier}");
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
/// Updates a scenario character's body-figure fields (BustSize, ButtSize) inside the
/// scenario's nested PayloadJson. Targeted, validated, transactional — fails with no changes if the
/// scenario or character is missing. Keeps the legacy BustMeasurement alias in sync so it cannot
/// clobber the canonical value on the next deserialization.
/// </summary>
static async Task<int> UpdateCharacterFigureAsync(
    SqliteConnection connection,
    string scenarioId,
    string characterName,
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
    Console.WriteLine($"Character figure updated: {characterName} in {scenarioId} -> bust={bustSize}, butt={buttSize}");
    return 0;
}

/// <summary>
/// One-time migration: maps the retired scenario-character <c>BodyType</c> value onto the body axes that replaced
/// it. <c>BodyType</c> conflated frame, fat and muscle into one list, so each legacy value is mapped to the axis it
/// actually described. Idempotent — a character that no longer carries <c>BodyType</c> is left alone. An
/// unrecognised value aborts the whole run with no changes: never a guessed default (repo no-fallback rule).
/// </summary>
static async Task<int> MigrateBodyAxesAsync(SqliteConnection connection)
{
    // "Curvy" is the operator's own definition (2026-09-22): normal weight with a fuller rear and a little belly,
    // NOT voluptuous. See memory/repo/body-vocabulary-curvy-meaning.md.
    var mapping = new Dictionary<string, (string? Adiposity, string? FatDistribution, string? MuscleMass, string? MuscleDefinition)>(StringComparer.OrdinalIgnoreCase)
    {
        ["Toned"] = ("lean, slim", null, null, "defined, visible abs"),
        ["Curvy"] = ("average weight", "fuller rear with a soft belly", null, null),
        ["Average"] = ("average weight", null, null, null)
    };

    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

    var migratedCharacters = 0;
    var migratedTemplates = 0;
    var updatedScenarios = 0;
    var skipped = 0;

    var (scenarios, skippedScenarios) = await ReadJsonPayloadsAsync(connection, transaction, "Scenarios");
    var (templates, skippedTemplates) = await ReadJsonPayloadsAsync(connection, transaction, "Templates");
    skipped = skippedScenarios + skippedTemplates;

    foreach (var (scenarioId, payload) in scenarios)
    {
        if (JsonNode.Parse(payload) is not JsonObject root || root["Characters"] is not JsonArray characters)
            continue;

        var changedInScenario = 0;
        foreach (var node in characters)
        {
            if (node is not JsonObject character
                || character["PhysicalAttributes"] is not JsonObject physical)
                continue;

            var name = character["Name"]?.GetValue<string>() ?? "<unnamed>";
            changedInScenario += ApplyBodyAxes(physical, mapping, $"character '{name}' in scenario '{scenarioId}'");
        }

        if (changedInScenario == 0)
            continue;

        migratedCharacters += changedInScenario;

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE Scenarios SET PayloadJson = $payload, UpdatedUtc = $now WHERE Id = $id;";
        update.Parameters.AddWithValue("$payload", root.ToJsonString());
        update.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        update.Parameters.AddWithValue("$id", scenarioId);
        await update.ExecuteNonQueryAsync();
        updatedScenarios++;
    }

    foreach (var (templateId, payload) in templates)
    {
        if (JsonNode.Parse(payload) is not JsonObject root
            || root["PhysicalAttributes"] is not JsonObject physical)
            continue;

        if (ApplyBodyAxes(physical, mapping, $"template '{templateId}'") == 0)
            continue;

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE Templates SET PayloadJson = $payload, UpdatedUtc = $now WHERE Id = $id;";
        update.Parameters.AddWithValue("$payload", root.ToJsonString());
        update.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        update.Parameters.AddWithValue("$id", templateId);
        await update.ExecuteNonQueryAsync();
        migratedTemplates++;
    }

    await transaction.CommitAsync();
    Console.WriteLine(
        $"Body-axes migration complete: {migratedCharacters} character(s) in {updatedScenarios} scenario(s) and "
        + $"{migratedTemplates} template(s) updated.");
    if (skipped > 0)
    {
        // Location templates store their payload as raw location prose rather than JSON, so they have no
        // PhysicalAttributes to migrate. Reported rather than silently ignored.
        Console.WriteLine($"Skipped {skipped} row(s) whose PayloadJson is not JSON (nothing to migrate in them).");
    }

    return 0;
}

/// <summary>
/// Reads every (Id, PayloadJson) pair from a payload-bearing table inside the caller's transaction, skipping rows
/// whose payload is not JSON, and reports how many were skipped so the omission is never silent.
/// </summary>
static async Task<(List<(string Id, string Payload)> Rows, int Skipped)> ReadJsonPayloadsAsync(
    SqliteConnection connection, SqliteTransaction transaction, string table)
{
    await using (var countNotJson = connection.CreateCommand())
    {
        countNotJson.Transaction = transaction;
        countNotJson.CommandText = $"SELECT COUNT(*) FROM {table} WHERE json_valid(PayloadJson) = 0;";
        var skipped = Convert.ToInt32(await countNotJson.ExecuteScalarAsync());

        var rows = new List<(string Id, string Payload)>();
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = $"SELECT Id, PayloadJson FROM {table} WHERE json_valid(PayloadJson) = 1;";
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetString(1)));

        return (rows, skipped);
    }
}

/// <summary>
/// Replaces a legacy <c>BodyType</c> with its axis values in place. Returns 1 when it migrated, 0 when there was
/// nothing to do, and throws (aborting the transaction with no changes) on a value that has no mapping.
/// </summary>
static int ApplyBodyAxes(
    JsonObject physical,
    Dictionary<string, (string? Adiposity, string? FatDistribution, string? MuscleMass, string? MuscleDefinition)> mapping,
    string where)
{
    if (physical["BodyType"] is not JsonValue legacyValue)
        return 0;

    var legacy = legacyValue.GetValue<string>();
    if (!mapping.TryGetValue(legacy, out var axes))
    {
        throw new InvalidOperationException(
            $"The {where} has BodyType '{legacy}', which has no body-axis mapping; no changes were made. Add the "
            + "mapping explicitly rather than guessing.");
    }

    physical.Remove("BodyType");
    if (axes.Adiposity is not null) physical["Adiposity"] = axes.Adiposity;
    if (axes.FatDistribution is not null) physical["FatDistribution"] = axes.FatDistribution;
    if (axes.MuscleMass is not null) physical["MuscleMass"] = axes.MuscleMass;
    if (axes.MuscleDefinition is not null) physical["MuscleDefinition"] = axes.MuscleDefinition;
    return 1;
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
    Console.Error.WriteLine("Commands: tables, schema [table], sessions, session <id>, adaptive <id>, themes <id>, evals <id>, transitions <id>, turns <id>, debug <id>, completions <id>, formula <id>, scenario <id>, gate-profiles, gate-rules <themeId>, theme-profiles, rp-themes <profileId>, provider-endpoint-update <providerId> <expectedCurrentBaseUrl> <newBaseUrl>, provider-split-model <sourceProviderId> <modelId> <newProviderName> <newBaseUrl>, provider-timeout-update <providerId> <expectedCurrentTimeoutSeconds> <newTimeoutSeconds>, b100-analyzer-configure, biglust-image-configure, b137-krea2-configure, qwen-edit-serverless-configure, qwen-edit-local-aio-configure, qwen-edit-local-aio-lora-configure <loraName> <strength>, qwen-edit-remix-aio-configure, qwen-edit-remix-aio-lora-configure <loraName> <strength>, local-comfyui-configure <baseUrl>, set-identity-strength <modelIdentifier> <strength>, character-figure-update <scenarioId> <characterName> <bustSize> <buttSize>, body-axes-migrate, api-image-configure, api-image-catalog, turn-membership-reconcile <sessionId>, b100-settle-plan <planId>, scene-asset-retag <assetId> <expectedCurrentType> <newType>, modelmanager-export [outFile], modelmanager-import <jsonFile>, sql <file> [id]");
}
