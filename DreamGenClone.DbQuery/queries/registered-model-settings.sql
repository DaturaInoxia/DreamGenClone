SELECT
    rm.Id,
    rm.ModelIdentifier,
    rm.DisplayName,
    rm.IsEnabled,
    rm.ContextWindowSize,
    rm.SupportsThinkingControl,
    rm.ModelKind,
    p.Name AS ProviderName
FROM RegisteredModels rm
JOIN Providers p ON p.Id = rm.ProviderId
WHERE rm.ModelIdentifier = '{{id}}'
ORDER BY p.Name, rm.DisplayName;
