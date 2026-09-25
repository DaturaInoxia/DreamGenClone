-- The state of the completed pack: both pointers and one approved asset per canonical slot in each half.
SELECT p.Id, p.Status, p.PackScope, p.CanonicalFaceAssetId, p.CanonicalFullBodyAssetId, p.ApprovedUtc,
       (SELECT COUNT(*) FROM SceneImageReferenceAssets a
         WHERE a.IdentityPackId = p.Id AND a.AssetKind = 'Face') AS FaceSlots,
       (SELECT COUNT(*) FROM SceneImageReferenceAssets a
         WHERE a.IdentityPackId = p.Id AND a.AssetKind = 'Face' AND a.IsApproved = 1) AS ApprovedFaces,
       (SELECT COUNT(*) FROM SceneImageReferenceAssets a
         WHERE a.IdentityPackId = p.Id AND a.AssetKind = 'FullBody') AS BodySlots,
       (SELECT COUNT(*) FROM SceneImageReferenceAssets a
         WHERE a.IdentityPackId = p.Id AND a.AssetKind = 'FullBody' AND a.IsApproved = 1) AS ApprovedBodies
FROM CharacterImageIdentityPacks p
WHERE p.Id = '2d13c667-a690-4b5a-992a-93359591aa41';
