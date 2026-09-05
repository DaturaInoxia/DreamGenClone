UPDATE RegisteredModels
SET ContextWindowSize = CASE WHEN ContextWindowSize < 65536 THEN 65536 ELSE ContextWindowSize END,
    MaximumContextTokens = CASE WHEN COALESCE(MaximumContextTokens, 0) < 65536 THEN 65536 ELSE MaximumContextTokens END
WHERE Id = (
    SELECT ModelId
    FROM FunctionModelDefaults
    WHERE FunctionName = 'RolePlaySceneBeatAnalyzer'
);
