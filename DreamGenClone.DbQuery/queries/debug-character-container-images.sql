-- Every image belonging to any of this character's builds' containers, newest first. {{id}} is the character
-- template id. Shows what a front render / edit actually produced and WHERE it landed (AssetId, batch, lineage).
SELECT i.Id, i.AssetId, i.Kind, i.Status, i.CandidateDecision, i.CandidateBatchId, i.SourceImageId,
       i.CreatedUtc, i.Width, i.Height, i.ByteLength, i.PromptCompilerId,
       substr(COALESCE(i.ErrorMessage, ''), 1, 60) AS Err
FROM SceneAssetImages i
WHERE i.AssetId IN (
        SELECT b.FrontContainerAssetId FROM CharacterIdentityBuilds b WHERE b.CharacterProfileId = '{{id}}'
        UNION
        SELECT b.BatchId FROM CharacterIdentityBuilds b WHERE b.CharacterProfileId = '{{id}}'
    )
ORDER BY i.CreatedUtc DESC
LIMIT 30;
