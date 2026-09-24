-- Rule out a whitespace/asset-table explanation for the one post-restart edit whose row is gone.
SELECT 'SceneImages' AS TableName, Id, Status FROM SceneImages WHERE Id LIKE '%c49b4875%'
UNION ALL
SELECT 'SceneAssetImages', Id, Status FROM SceneAssetImages WHERE Id LIKE '%c49b4875%'
UNION ALL
SELECT 'ProducedImages', Id, Status FROM ProducedImages WHERE Id LIKE '%c49b4875%';
