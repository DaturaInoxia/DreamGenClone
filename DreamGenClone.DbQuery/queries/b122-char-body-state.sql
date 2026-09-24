-- Live body-acquisition state for one character: its builds. {{id}} is the characterProfileId (or a buildId).
SELECT b.Id, b.CharacterProfileId, b.TargetKind, b.CurrentStep, b.Status, b.BatchId,
       b.FrontContainerAssetId, b.CreatedUtc, b.UpdatedUtc
FROM CharacterIdentityBuilds b
WHERE b.CharacterProfileId = '{{id}}' OR b.Id = '{{id}}'
ORDER BY b.CreatedUtc DESC;
