SELECT 'SceneAssets.Id' AS Where_, Id AS Value FROM SceneAssets WHERE Id = '93eae67af55d417ca3a69da8bcc19f0a'
UNION ALL SELECT 'SceneAssetImages.Id', Id FROM SceneAssetImages WHERE Id = '93eae67af55d417ca3a69da8bcc19f0a'
UNION ALL SELECT 'SceneAssetImages.SourceImageId', Id FROM SceneAssetImages WHERE SourceImageId = '93eae67af55d417ca3a69da8bcc19f0a'
UNION ALL SELECT 'SceneAssetImages.AssetId', Id FROM SceneAssetImages WHERE AssetId = '93eae67af55d417ca3a69da8bcc19f0a'
UNION ALL SELECT 'SceneAssetImages.CandidateBatchId', Id FROM SceneAssetImages WHERE CandidateBatchId = '93eae67af55d417ca3a69da8bcc19f0a'
UNION ALL SELECT 'CharacterIdentityAngles.OutputArtifactId', Id FROM CharacterIdentityAngles WHERE OutputArtifactId = '93eae67af55d417ca3a69da8bcc19f0a'
UNION ALL SELECT 'CharacterIdentityAngleAttempts.Id', Id FROM CharacterIdentityAngleAttempts WHERE Id = '93eae67af55d417ca3a69da8bcc19f0a'
UNION ALL SELECT 'CharacterIdentityAngleAttempts.OutputArtifactId', Id FROM CharacterIdentityAngleAttempts WHERE OutputArtifactId = '93eae67af55d417ca3a69da8bcc19f0a';
