-- Every image in this build's body candidate batches, with its decision: what the review deck lists and what the
-- view rows accept. {{id}} is the buildId.
SELECT i.Id, i.AssetId, i.CandidateBatchId, i.CandidateDecision, i.Status, i.CreatedUtc,
       substr(coalesce(i.Prompt, ''), 1, 50) AS PromptStart
FROM SceneAssetImages i
WHERE i.CandidateBatchId LIKE '%' || '{{id}}' || '%'
ORDER BY i.CreatedUtc DESC;
