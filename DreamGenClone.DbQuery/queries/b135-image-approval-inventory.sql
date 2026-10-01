-- What production-approval values exist on images at all, and where. Answers whether the approved-reference
-- picker's filter can EVER pass on this store, for any asset type.
SELECT
    s.Type                                                            AS AssetType,
    COALESCE(i.ProductionApprovalStatus, '(null)')                    AS ImageApproval,
    CASE WHEN i.ProductionVersion IS NULL THEN 'version=null' ELSE 'version=' || i.ProductionVersion END AS Version,
    COUNT(*)                                                          AS Images
FROM SceneAssetImages i
JOIN SceneAssets s ON s.Id = i.AssetId
GROUP BY s.Type, COALESCE(i.ProductionApprovalStatus, '(null)'), CASE WHEN i.ProductionVersion IS NULL THEN 'version=null' ELSE 'version=' || i.ProductionVersion END
ORDER BY s.Type, ImageApproval;
