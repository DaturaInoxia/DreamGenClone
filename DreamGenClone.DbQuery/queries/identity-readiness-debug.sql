SELECT
  g.Id AS ProductionGroupId,
  g.SessionId,
  g.InteractionId,
  g.MomentEnrichmentId,
  g.IdentityPolicy,
  g.IdentitySkipReason,
  e.Status AS EnrichmentStatus,
  e.FrozenStateContractJson,
  p.Id AS PackId,
  p.CharacterProfileId,
  p.Version,
  p.Status AS PackStatus,
  p.CanonicalFaceAssetId,
  a.AssetKind,
  a.IsApproved AS AssetApproved,
  a.IdentityPackId AS AssetPackId,
  a.FileRelativePath,
  a.Sha256,
  cp.Name AS ProfileName
FROM SceneImageProductionGroups g
LEFT JOIN SceneMomentEnrichments e ON e.Id = g.MomentEnrichmentId
LEFT JOIN CharacterImageIdentityPacks p ON p.Status = 'Approved'
LEFT JOIN SceneImageReferenceAssets a ON a.Id = p.CanonicalFaceAssetId
LEFT JOIN CharacterProfiles cp ON cp.Id = p.CharacterProfileId
WHERE g.SessionId = '4f2eec18-b190-4beb-ad35-8d520ae5c800'
ORDER BY g.CreatedUtc DESC, p.Version DESC;
