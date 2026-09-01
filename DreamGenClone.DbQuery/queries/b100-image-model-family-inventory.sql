SELECT
    rm.Id,
    rm.DisplayName,
    rm.ModelIdentifier,
    rm.ModelKind,
    (SELECT COUNT(*) FROM pragma_table_info('RegisteredModels') WHERE name = 'SceneImageModelFamily') AS HasSceneImageModelFamily,
    (SELECT COUNT(*) FROM pragma_table_info('RegisteredModels') WHERE name = 'PromptDialect') AS HasPromptDialect,
    rm.IsEnabled,
    p.Name AS ProviderName,
    f.FunctionName
FROM RegisteredModels rm
INNER JOIN Providers p ON p.Id = rm.ProviderId
LEFT JOIN FunctionModelDefaults f ON f.ModelId = rm.Id
WHERE rm.ModelKind = 1
ORDER BY rm.DisplayName, f.FunctionName;
