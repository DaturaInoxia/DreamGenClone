-- What the composer's APPROVED-SCENE-ASSET dropdown can list for a Face or Body slot: one row per
-- character-owned SceneAsset of that type that carries an image the ReferencePicker accepts
-- (Complete + ProductionApproved + versioned + hashed). An empty result means that dropdown renders
-- with only its "Text only / no asset reference" entry, which reads as an empty control.
SELECT
    s.CharacterProfileId                                              AS Character,
    s.Type                                                            AS AssetType,
    COUNT(DISTINCT s.Id)                                              AS Assets,
    SUM(CASE WHEN i.Id IS NOT NULL THEN 1 ELSE 0 END)                 AS UsableImages
FROM SceneAssets s
LEFT JOIN SceneAssetImages i
       ON i.AssetId = s.Id
      AND i.Status = 'Complete'
      AND i.ProductionApprovalStatus = 'Approved'
      AND i.ProductionVersion IS NOT NULL
      AND COALESCE(i.Sha256, '') <> ''
WHERE s.Type IN ('CharacterFace', 'CharacterBody')
GROUP BY s.CharacterProfileId, s.Type
ORDER BY s.CharacterProfileId, s.Type;
