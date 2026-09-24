SELECT b.Id AS BuildId, b.CurrentStep, b.FrontContainerAssetId, b.CanonicalFrontAssetId,
       i.Id AS ImageId, i.Kind, i.Status, i.CandidateDecision,
       substr(i.ValidationResultJson, 1, 300) AS Validation,
       i.CreatedUtc
FROM CharacterIdentityBuilds b
LEFT JOIN SceneAssetImages i ON i.AssetId = b.FrontContainerAssetId
WHERE b.CharacterProfileId = 'f58f959a-8050-4388-a219-99d2df3446a1'
ORDER BY b.UpdatedUtc DESC, i.CreatedUtc;
