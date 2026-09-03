SELECT 'character_profiles' AS category, COUNT(*) AS total
FROM CharacterProfiles
UNION ALL
SELECT 'identity_packs', COUNT(*)
FROM CharacterImageIdentityPacks
UNION ALL
SELECT 'production_source_assets', COUNT(*)
FROM SceneAssets
WHERE SourceApprovalDecisionId IS NOT NULL
UNION ALL
SELECT 'enabled_image_models', COUNT(*)
FROM RegisteredModels model
JOIN Providers provider ON provider.Id = model.ProviderId
WHERE model.IsEnabled = 1 AND provider.IsEnabled = 1 AND model.ModelKind = 1
UNION ALL
SELECT 'enabled_image_providers', COUNT(*)
FROM Providers
WHERE IsEnabled = 1 AND ImageCapability <> 0;
