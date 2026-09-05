WITH frozen_characters AS (
  SELECT
    g.Id AS ProductionGroupId,
    g.InteractionId,
    json_extract(character.value, '$.characterId') AS CharacterId,
    json_extract(character.value, '$.name') AS CharacterName
  FROM SceneImageProductionGroups g
  JOIN SceneMomentEnrichments e ON e.Id = g.MomentEnrichmentId
  JOIN json_each(json_extract(e.FrozenStateContractJson, '$.characters')) AS character
  WHERE g.SessionId = '4f2eec18-b190-4beb-ad35-8d520ae5c800'
    AND g.InteractionId = '30ff1428-43f6-4646-9104-9373592c4d45'
)
SELECT
  f.ProductionGroupId,
  f.InteractionId,
  f.CharacterId,
  f.CharacterName,
  p.Id AS PackId,
  p.CharacterProfileId,
  p.Version,
  p.Status AS PackStatus,
  p.CanonicalFaceAssetId,
  a.AssetKind,
  a.IsApproved AS FaceApproved,
  a.IdentityPackId AS FacePackId,
  a.FileRelativePath,
  a.Sha256
FROM frozen_characters f
LEFT JOIN CharacterImageIdentityPacks p ON p.CharacterProfileId = f.CharacterId
LEFT JOIN SceneImageReferenceAssets a ON a.Id = p.CanonicalFaceAssetId
ORDER BY f.CharacterName, p.Version DESC;
