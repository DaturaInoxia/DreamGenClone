SELECT
    rm.Id,
    rm.ModelIdentifier,
    rm.DisplayName,
    p.Name AS ProviderName,
    rm.IsEnabled,
    rm.ModelKind,
    rm.SupportsThinkingControl,
    rm.ContextWindowSize,
    group_concat(f.FunctionName, ', ') AS AssignedFunctions
FROM RegisteredModels rm
JOIN Providers p ON p.Id = rm.ProviderId
LEFT JOIN FunctionModelDefaults f ON f.ModelId = rm.Id
GROUP BY rm.Id, rm.ModelIdentifier, rm.DisplayName, p.Name, rm.IsEnabled,
         rm.ModelKind, rm.SupportsThinkingControl, rm.ContextWindowSize
ORDER BY rm.IsEnabled DESC, rm.ModelKind, p.Name, rm.DisplayName;
