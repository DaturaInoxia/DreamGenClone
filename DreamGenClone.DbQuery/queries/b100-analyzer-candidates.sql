SELECT
    rm.Id AS ModelId,
    rm.DisplayName,
    rm.ModelIdentifier,
    p.Name AS ProviderName,
    rm.IsEnabled AS ModelEnabled,
    p.IsEnabled AS ProviderEnabled,
    rm.ModelKind,
    rm.SupportsThinkingControl,
    rm.StructuredOutputMode,
    rm.MaximumContextTokens,
    rm.MaximumOutputTokens,
    p.TimeoutSeconds,
    CASE WHEN p.ApiKeyEncrypted IS NULL OR trim(p.ApiKeyEncrypted) = '' THEN 0 ELSE 1 END AS HasEncryptedApiKey
FROM RegisteredModels rm
JOIN Providers p ON p.Id = rm.ProviderId
WHERE rm.ModelKind = 0
ORDER BY
    rm.StructuredOutputMode DESC,
    rm.IsEnabled DESC,
    p.IsEnabled DESC,
    p.Name,
    rm.DisplayName;
