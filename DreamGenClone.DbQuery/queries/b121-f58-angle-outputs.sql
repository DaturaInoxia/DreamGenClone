SELECT i.Id, i.AssetId, i.Kind, i.Status, i.CandidateBatchId, i.Sha256, i.Width, i.Height, i.CreatedUtc,
       a.Name AS ContainerName, a.Kind AS ContainerKind
FROM SceneAssetImages i
LEFT JOIN SceneAssets a ON a.Id = i.AssetId
WHERE i.Id IN ('93c4c9dcfebd4181824db3eed7928199', '632a5e226b044b0d84b448fb3f86c5fc',
               'edfb0b69ef4546f0b26fab8a029e9367', '41c85cc2da3a4499a33932f97a924b91')
ORDER BY i.CreatedUtc;
