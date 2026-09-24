SELECT s.Step, s.Status, s.InputArtifactId, s.OutputArtifactId, s.ManualOverrideApplied,
       s.FailureReason, s.CreatedUtc, s.UpdatedUtc,
       (SELECT COUNT(*) FROM SceneAssetImages i WHERE i.Id = s.OutputArtifactId) AS OutputImageExists,
       (SELECT COUNT(*) FROM SceneAssetImages i WHERE i.Id = s.InputArtifactId) AS InputImageExists
FROM CharacterIdentityBuildSteps s
WHERE s.BuildId = 'b8adc0e742e645f7a4a7100ff4796a94'
ORDER BY s.Step;
