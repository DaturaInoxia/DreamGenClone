-- Every image in this character's builds' containers, newest first, with the lineage fields that decide where an
-- edit-chain image lands (Kind, SourceImageId, CandidateBatchId). {{id}} is the character template id stored in the
-- legacy-named CharacterProfileId column.
SELECT b.Id AS BuildId, b.TargetKind, b.FrontContainerAssetId, b.CurrentStep,
       i.Id AS ImageId, i.Kind, i.Status, i.CandidateDecision, i.SourceImageId, i.CandidateBatchId,
       i.Width, i.Height, i.CreatedUtc, substr(COALESCE(i.ErrorMessage, ''), 1, 80) AS Err
FROM CharacterIdentityBuilds b
LEFT JOIN SceneAssetImages i ON i.AssetId = b.FrontContainerAssetId
WHERE b.CharacterProfileId = '{{id}}'
ORDER BY i.CreatedUtc DESC
LIMIT 60;
