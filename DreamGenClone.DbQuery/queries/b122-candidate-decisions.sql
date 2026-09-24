-- What the operator has actually DECIDED in this build's candidate batches, by decision value, plus the image each
-- body view currently points at. {{id}} is the buildId.
SELECT i.CandidateDecision, COUNT(*) AS Images
FROM SceneAssetImages i
WHERE i.CandidateBatchId LIKE '%' || '{{id}}' || '%'
GROUP BY i.CandidateDecision;
