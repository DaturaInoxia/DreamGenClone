-- Which referencing rows actually hold the container 62b06cf6b76b48b586ebcb35e83bc699 (and its 32 images).
SELECT 'EditSession.OwnImages'  AS Held, CAST(COUNT(*) AS TEXT) AS Rows
FROM SceneAssetImageEditSessions WHERE AssetId = '62b06cf6b76b48b586ebcb35e83bc699'
UNION ALL
SELECT 'EditSession.OtherAssetUsingOurImage', CAST(COUNT(*) AS TEXT)
FROM SceneAssetImageEditSessions
WHERE AssetId <> '62b06cf6b76b48b586ebcb35e83bc699'
  AND SourceImageId IN (SELECT Id FROM SceneAssetImages WHERE AssetId = '62b06cf6b76b48b586ebcb35e83bc699')
UNION ALL
SELECT 'EditSession.OwnImageAsSource', CAST(COUNT(*) AS TEXT)
FROM SceneAssetImageEditSessions
WHERE SourceImageId IN (SELECT Id FROM SceneAssetImages WHERE AssetId = '62b06cf6b76b48b586ebcb35e83bc699')
UNION ALL
SELECT 'LoraDatasetMember', CAST(COUNT(*) AS TEXT)
FROM CharacterLoraDatasetMembers WHERE SceneAssetId = '62b06cf6b76b48b586ebcb35e83bc699'
UNION ALL
SELECT 'OrderedMediaReferenceBinding', CAST(COUNT(*) AS TEXT)
FROM OrderedMediaReferenceBindings WHERE SceneAssetId = '62b06cf6b76b48b586ebcb35e83bc699'
UNION ALL
SELECT 'ProductionDerivative', CAST(COUNT(*) AS TEXT)
FROM ProductionDerivatives WHERE SceneAssetId = '62b06cf6b76b48b586ebcb35e83bc699'
UNION ALL
SELECT 'MediaEditSession.OnOurImage', CAST(COUNT(*) AS TEXT)
FROM MediaEditSessions
WHERE SourceImageId IN (SELECT Id FROM SceneAssetImages WHERE AssetId = '62b06cf6b76b48b586ebcb35e83bc699');
