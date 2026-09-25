-- This character's identity packs, newest first, with how many assets each carries — the promoted body-complete pack
-- is what B-122 phase 7 produces. {{id}} is the characterProfileId.
SELECT p.Id, p.Version, p.Status, p.PackScope, p.SupersedesId, p.CanonicalFaceAssetId,
       (SELECT COUNT(*) FROM SceneImageReferenceAssets a WHERE a.IdentityPackId = p.Id) AS Assets
FROM CharacterImageIdentityPacks p
WHERE p.CharacterProfileId = '{{id}}'
ORDER BY p.Version DESC;
