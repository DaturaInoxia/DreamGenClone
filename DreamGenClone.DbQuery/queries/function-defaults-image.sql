SELECT f.Id, f.FunctionName, f.ModelId, f.Temperature, f.MaxTokens, f.MaxConcurrentJobs,
       m.ModelIdentifier AS ModelName, m.ModelKind
FROM FunctionModelDefaults f
LEFT JOIN RegisteredModels m ON m.Id = f.ModelId
ORDER BY f.FunctionName;
