-- Everything that would block deleting the container 62b06cf6b76b48b586ebcb35e83bc699 from the asset store:
-- (a) every table with a foreign key into SceneAssets or SceneAssetImages, and
-- (b) how many of those rows actually point at this container or at one of its images.
SELECT 'REFERENCES SceneAssets' AS Direction, name AS TableName, sql AS Definition
FROM sqlite_master
WHERE type = 'table' AND sql LIKE '%REFERENCES SceneAssets%'
UNION ALL
SELECT 'REFERENCES SceneAssetImages', name, sql
FROM sqlite_master
WHERE type = 'table' AND sql LIKE '%REFERENCES SceneAssetImages%'
UNION ALL
SELECT 'IMAGE ROWS ON THIS CONTAINER', CAST(COUNT(*) AS TEXT), 'SceneAssetImages'
FROM SceneAssetImages WHERE AssetId = '62b06cf6b76b48b586ebcb35e83bc699'
UNION ALL
SELECT 'IMAGES DERIVED FROM THIS CONTAINER''S IMAGES', CAST(COUNT(*) AS TEXT), 'SceneAssetImages.SourceImageId'
FROM SceneAssetImages
WHERE SourceImageId IN (SELECT Id FROM SceneAssetImages WHERE AssetId = '62b06cf6b76b48b586ebcb35e83bc699')
UNION ALL
SELECT 'ASSETS DERIVED FROM THIS CONTAINER', CAST(COUNT(*) AS TEXT), 'SceneAssets.SourceAssetId'
FROM SceneAssets WHERE SourceAssetId = '62b06cf6b76b48b586ebcb35e83bc699';
