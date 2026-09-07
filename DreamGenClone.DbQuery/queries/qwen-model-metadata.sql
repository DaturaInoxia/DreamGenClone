SELECT
    p.Id AS ProviderId,
    p.Name AS ProviderName,
    p.IsEnabled AS ProviderEnabled,
    rm.Id AS ModelId,
    rm.ModelIdentifier,
    rm.DisplayName,
    rm.IsEnabled AS ModelEnabled,
    rm.ModelKind,
    rm.SceneImageModelFamily,
    rm.PromptDialect,
    f.FunctionName,
    f.ModelId AS FunctionModelId
FROM Providers p
LEFT JOIN RegisteredModels rm ON rm.ProviderId = p.Id
LEFT JOIN FunctionModelDefaults f ON f.ModelId = rm.Id
WHERE lower(p.Name) = 'openrouter'
  AND lower(rm.ModelIdentifier) = 'qwen/qwen-image-3-pro';