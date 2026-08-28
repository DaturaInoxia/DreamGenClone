SELECT f.Id AS DefaultId, f.FunctionName, f.ModelId, f.Temperature, f.TopP, f.MaxTokens,
    f.ThinkingMode, f.MaxConcurrentJobs, f.UpdatedUtc
FROM FunctionModelDefaults f
WHERE f.FunctionName LIKE '%Image%' OR f.FunctionName LIKE '%Scene%' OR f.FunctionName LIKE '%Pose%' OR f.FunctionName LIKE '%Vision%'
ORDER BY f.FunctionName;
