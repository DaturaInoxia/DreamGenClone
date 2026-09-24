SELECT 'CONTAINER' AS RowKind, a.Id, a.Name, a.Kind, a.Status, a.IdentityPackId, a.CharacterProfileId, a.IsContainerOnly, a.CreatedUtc, a.UpdatedUtc
FROM SceneAssets a
WHERE a.Id = 'c04a3531496a4b4c9d2f3408762cbb90';

SELECT 'PROFILE' AS RowKind, p.Id, p.Name, p.ScenarioId, p.CreatedUtc, p.UpdatedUtc
FROM CharacterProfiles p
WHERE p.Id = 'f58f959a-8050-4388-a219-99d2df3446a1';

SELECT 'BUILD-FRONT-IMAGE' AS RowKind, i.Id, i.AssetId, i.Status, i.Kind, i.FileRelativePath, i.CreatedUtc
FROM SceneAssetImages i
WHERE i.Id = '93eae67af55d417ca3a69da8bcc19f0a';
