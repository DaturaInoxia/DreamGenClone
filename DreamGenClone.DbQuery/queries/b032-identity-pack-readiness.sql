SELECT pack.Id AS IdentityPackId,
       pack.Version,
       pack.Status,
       pack.ApprovedUtc,
       character.Id AS CharacterProfileId,
       character.Name AS CharacterName,
       pack.CanonicalFaceAssetId,
       asset.Status AS FaceAssetStatus,
       asset.SourceApprovalDecisionId,
       asset.FileRelativePath,
       asset.Sha256
FROM CharacterImageIdentityPacks pack
LEFT JOIN CharacterProfiles character ON character.Id = pack.CharacterProfileId
LEFT JOIN SceneAssets asset ON asset.Id = pack.CanonicalFaceAssetId
ORDER BY pack.CreatedUtc DESC;
