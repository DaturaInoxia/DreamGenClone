SELECT i.Id, i.AssetId, i.Kind, i.Status, i.CandidateDecision, i.FileRelativePath,
       substr(i.ValidationResultJson, 1, 200) AS Validation,
       a.Name AS AssetName, a.Kind AS AssetKind, a.IdentityPackId,
       i.CreatedUtc
FROM SceneAssetImages i
LEFT JOIN SceneAssets a ON a.Id = i.AssetId
WHERE i.Id IN ('93eae67af55d417ca3a69da8bcc19f0a', '7c3a6c8dda004c398904b1fd92fb6ba7');
