SELECT
    f.FunctionName,
    f.ModelId,
    rm.ModelIdentifier,
    rm.DisplayName,
    p.Name AS ProviderName,
    f.Temperature,
    f.TopP,
    f.MaxTokens,
    f.ThinkingMode,
    rm.SupportsThinkingControl,
    rm.ContextWindowSize,
    rm.IsEnabled
FROM FunctionModelDefaults f
JOIN RegisteredModels rm ON rm.Id = f.ModelId
JOIN Providers p ON p.Id = rm.ProviderId
WHERE f.FunctionName = '{{id}}';
