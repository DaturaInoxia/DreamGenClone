SELECT AssetKind, COUNT(*) AS Count
FROM SceneImageReferenceAssets
GROUP BY AssetKind
ORDER BY AssetKind;
