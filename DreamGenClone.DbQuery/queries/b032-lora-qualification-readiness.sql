SELECT 'datasets' AS category,
       COUNT(*) AS total,
       SUM(CASE WHEN Status = 'Frozen' THEN 1 ELSE 0 END) AS ready
FROM CharacterLoraDatasets
UNION ALL
SELECT 'successful_training_attempts',
       COUNT(*),
       SUM(CASE WHEN Status = 'Succeeded' THEN 1 ELSE 0 END)
FROM CharacterLoraTrainingAttempts
UNION ALL
SELECT 'lora_artifacts',
       COUNT(*),
       SUM(CASE WHEN Status = 'Qualified' THEN 1 ELSE 0 END)
FROM CharacterLoraArtifacts
UNION ALL
SELECT 'identity_capability_profiles',
       COUNT(*),
       SUM(CASE WHEN Status = 'Qualified' AND Enabled = 1 THEN 1 ELSE 0 END)
FROM MediaCapabilityProfiles
WHERE json_array_length(COALESCE(json_extract(PayloadJson, '$.supportedIdentityStrategiesJson'), '[]')) > 0
UNION ALL
SELECT 'lora_or_combined_cells',
       COUNT(*),
       SUM(CASE WHEN Status = 'Qualified' THEN 1 ELSE 0 END)
FROM MediaCapabilityCells
WHERE json_extract(PayloadJson, '$.identityStrategyKind') IN (2, 3, 'Lora', 'Combined');

SELECT Id, CharacterProfileId, DatasetId, Version, BaseModelId, BaseModelVersion, Status,
       Sha256, QualifiedUtc
FROM CharacterLoraArtifacts
ORDER BY CreatedUtc DESC;

SELECT Id, ModelId, ModelVersion, Status, Enabled,
       json_extract(PayloadJson, '$.registeredModelId') AS RegisteredModelId,
       json_extract(PayloadJson, '$.supportedIdentityStrategiesJson') AS SupportedStrategies
FROM MediaCapabilityProfiles
WHERE json_array_length(COALESCE(json_extract(PayloadJson, '$.supportedIdentityStrategiesJson'), '[]')) > 0
ORDER BY CreatedUtc DESC;

SELECT Id, CapabilityProfileId, ActorCount, Status, EvidenceRunId,
       json_extract(PayloadJson, '$.identityStrategyKind') AS IdentityStrategyKind,
       FailureReason
FROM MediaCapabilityCells
WHERE json_extract(PayloadJson, '$.identityStrategyKind') IN (2, 3, 'Lora', 'Combined')
ORDER BY CreatedUtc DESC;
