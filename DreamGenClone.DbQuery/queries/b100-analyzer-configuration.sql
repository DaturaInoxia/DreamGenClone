SELECT
    f.Id AS FunctionDefaultId,
    f.FunctionName,
    f.ModelId,
    f.MaxTokens,
    f.ThinkingMode,
    f.MaxConcurrentJobs,
    f.DurableJobLeaseSeconds,
    f.DurableJobPollIntervalMilliseconds,
    f.TransientRetryCount,
    f.TransientRetryDelaysSecondsJson,
    f.DiagnosticsRetentionDays,
    f.MaximumCatalogueEntries,
    rm.DisplayName AS ModelName,
    rm.ModelIdentifier,
    rm.ModelKind,
    rm.IsEnabled AS ModelEnabled,
    rm.SupportsThinkingControl,
    rm.StructuredOutputMode,
    rm.MaximumContextTokens,
    rm.MaximumOutputTokens,
    p.Id AS ProviderId,
    p.Name AS ProviderName,
    p.IsEnabled AS ProviderEnabled,
    p.BaseUrl,
    p.ChatCompletionsPath,
    p.TimeoutSeconds,
    CASE WHEN p.ApiKeyEncrypted IS NULL OR trim(p.ApiKeyEncrypted) = '' THEN 0 ELSE 1 END AS HasEncryptedApiKey
FROM FunctionModelDefaults f
LEFT JOIN RegisteredModels rm ON rm.Id = f.ModelId
LEFT JOIN Providers p ON p.Id = rm.ProviderId
WHERE f.FunctionName = 'RolePlaySceneBeatAnalyzer';
