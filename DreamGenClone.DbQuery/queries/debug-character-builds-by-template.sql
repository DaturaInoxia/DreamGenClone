-- The face/body builds of one character with their step rows, newest first. {{id}} is the character template id
-- (the identity key, what the studio's URL resolves to).
SELECT b.Id AS BuildId, b.TargetKind, b.CurrentStep, b.Status, b.FrontContainerAssetId, b.CanonicalFrontAssetId,
       b.ProducedIdentityPackId, b.UpdatedUtc,
       s.Step, s.Status AS StepStatus, s.InputArtifactId, s.OutputArtifactId, s.FailureReason
FROM CharacterIdentityBuilds b
LEFT JOIN CharacterIdentityBuildSteps s ON s.BuildId = b.Id
WHERE b.CharacterProfileId = '{{id}}'
ORDER BY b.UpdatedUtc DESC, b.Id, s.Step;
