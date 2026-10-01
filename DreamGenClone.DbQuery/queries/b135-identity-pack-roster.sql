-- The roster the composer's identity picker offers: every APPROVED pack, per character, with the
-- approved face and full-body references it can actually serve (the composer offers only these).
SELECT
    p.CharacterProfileId                                              AS Character,
    p.Version                                                         AS PackVersion,
    p.PackScope                                                       AS Scope,
    p.Status                                                          AS Status,
    (SELECT COUNT(*) FROM SceneImageReferenceAssets a
      WHERE a.IdentityPackId = p.Id AND a.IsApproved = 1 AND a.AssetKind = 'Face')     AS ApprovedFaces,
    (SELECT COUNT(*) FROM SceneImageReferenceAssets a
      WHERE a.IdentityPackId = p.Id AND a.IsApproved = 1 AND a.AssetKind = 'FullBody') AS ApprovedBodies,
    (SELECT GROUP_CONCAT(DISTINCT a.FaceView) FROM SceneImageReferenceAssets a
      WHERE a.IdentityPackId = p.Id AND a.IsApproved = 1 AND a.AssetKind = 'Face')     AS FaceViews,
    (SELECT GROUP_CONCAT(DISTINCT a.BodyView || '/' || COALESCE(a.BodyState, '?')) FROM SceneImageReferenceAssets a
      WHERE a.IdentityPackId = p.Id AND a.IsApproved = 1 AND a.AssetKind = 'FullBody') AS BodyViews
FROM CharacterImageIdentityPacks p
WHERE p.Status = 'Approved'
ORDER BY p.CharacterProfileId, p.Version DESC;
