-- The newest images in this build's container, whatever batch they landed in: shows what a queued render/edit
-- actually produced (or that nothing was created at all). {{id}} is the container assetId.
SELECT i.Id, i.CandidateBatchId, i.Kind, i.Status, i.CandidateDecision, i.CreatedUtc, i.ErrorMessage
FROM SceneAssetImages i
WHERE i.AssetId = '{{id}}'
ORDER BY i.CreatedUtc DESC
LIMIT 8;
