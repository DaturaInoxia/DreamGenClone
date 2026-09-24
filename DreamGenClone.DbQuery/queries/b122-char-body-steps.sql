-- The build's plan steps: what the workflow is waiting on. {{id}} is the buildId.
SELECT s.BuildId, s.Step, s.Status, s.InputArtifactId, s.OutputArtifactId, s.ResolvedModelId,
       s.FailureReason, s.CreatedUtc, s.UpdatedUtc
FROM CharacterIdentityBuildSteps s
WHERE s.BuildId = '{{id}}'
ORDER BY s.Step;
