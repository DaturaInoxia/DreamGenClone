UPDATE FunctionModelDefaults
SET DurableJobLeaseSeconds = 900,
    UpdatedUtc = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
WHERE FunctionName = 'RolePlaySceneBeatAnalyzer';
