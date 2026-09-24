SELECT a.Id AS AngleId, a.View, a.Status, a.InputArtifactId, a.OutputArtifactId, a.AcceptedAttemptId,
       a.UpdatedUtc, i.FileRelativePath AS OutputFile,
       (SELECT COUNT(*) FROM CharacterIdentityAngleAttempts t WHERE t.AngleId = a.Id) AS AttemptCount
FROM CharacterIdentityAngles a
LEFT JOIN SceneAssetImages i ON i.Id = a.OutputArtifactId
WHERE a.BuildId = 'b8adc0e742e645f7a4a7100ff4796a94'
ORDER BY a.View;
