-- Every character-owned face/body asset with the state of its images, so "is there anything to bind?"
-- can be answered from the rows rather than from the picker's own filter.
SELECT
    s.CharacterProfileId                                     AS Character,
    s.Type                                                   AS AssetType,
    s.Id                                                     AS AssetId,
    s.Name                                                   AS AssetName,
    s.FaceView                                               AS FaceView,
    s.BodyView || '/' || COALESCE(s.BodyState, '?')          AS BodyViewState,
    (SELECT COUNT(*) FROM SceneAssetImages i WHERE i.AssetId = s.Id) AS Images,
    (SELECT GROUP_CONCAT(COALESCE(i.Status, '?') || ':' || COALESCE(i.ProductionApprovalStatus, '-') || ':v' || COALESCE(i.ProductionVersion, 0) || ':' || CASE WHEN COALESCE(i.Sha256, '') = '' THEN 'nohash' ELSE 'hash' END, ' | ')
       FROM SceneAssetImages i WHERE i.AssetId = s.Id)        AS ImageStates
FROM SceneAssets s
WHERE s.Type IN ('CharacterFace', 'CharacterBody')
  AND s.FaceView IS NOT NULL
  AND s.FaceView IN ('ThreeQuarterLeft', 'Front')
ORDER BY s.CharacterProfileId, s.Type, s.FaceView
LIMIT 60;
