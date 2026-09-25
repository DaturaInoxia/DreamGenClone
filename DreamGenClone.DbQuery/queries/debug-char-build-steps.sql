-- The face builds of this character with their step rows, so the step index and the override state are visible
-- exactly as the studio reads them. {{id}} is the character template id (legacy CharacterProfileId column).
SELECT b.Id AS BuildId, b.TargetKind, b.CurrentStep, b.Status AS BuildStatus,
       b.FrontContainerAssetId, b.CanonicalFrontAssetId,
       s.Step, s.Status AS StepStatus, s.InputArtifactId, s.OutputArtifactId,
       s.ManualOverrideApplied, b.UpdatedUtc
FROM CharacterIdentityBuilds b
LEFT JOIN CharacterIdentityBuildSteps s ON s.BuildId = b.Id
WHERE b.CharacterProfileId = '{{id}}'
ORDER BY b.CreatedUtc DESC, s.Step;
