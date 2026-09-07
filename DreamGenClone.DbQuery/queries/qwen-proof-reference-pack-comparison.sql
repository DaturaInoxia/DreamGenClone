SELECT pack.Id AS IdentityPackId,
       pack.Version,
       pack.Status,
       profile.Name AS CharacterName,
       asset.Id AS CanonicalFaceAssetId,
       asset.AssetKind,
       asset.IsApproved,
       asset.FileRelativePath,
       asset.Sha256
FROM CharacterImageIdentityPacks pack
LEFT JOIN CharacterProfiles profile ON profile.Id = pack.CharacterProfileId
LEFT JOIN SceneImageReferenceAssets asset ON asset.Id = pack.CanonicalFaceAssetId
WHERE pack.Status = 'Approved'
ORDER BY profile.Name, pack.Version DESC;