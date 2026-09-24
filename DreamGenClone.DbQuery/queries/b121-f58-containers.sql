SELECT a.Id AS AssetId, a.Name, a.Kind AS AssetKind, a.Status AS AssetStatus, a.IdentityPackId,
       a.IsContainerOnly, a.CreatedUtc AS AssetCreated, a.UpdatedUtc AS AssetUpdated,
       (SELECT COUNT(*) FROM SceneAssetImages i WHERE i.AssetId = a.Id) AS ImageCount
FROM SceneAssets a
WHERE a.CharacterProfileId = 'f58f959a-8050-4388-a219-99d2df3446a1'
ORDER BY a.CreatedUtc DESC;
