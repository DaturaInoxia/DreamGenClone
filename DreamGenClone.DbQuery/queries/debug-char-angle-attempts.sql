-- The angle rows and their attempts for this character's face builds, with the artifacts each names, so the card's
-- list can be compared with what the edit/crop/enhance chain actually produced. {{id}} is the character template id.
SELECT b.Id AS BuildId, b.CurrentStep,
       a.View, a.Status AS AngleStatus, a.InputArtifactId, a.OutputArtifactId, a.AcceptedAttemptId,
       t.AttemptNumber, t.Status AS AttemptStatus, t.InputArtifactId AS AttemptInput,
       t.OutputArtifactId AS AttemptOutput, t.CreatedUtc, substr(COALESCE(t.FailureReason, ''), 1, 60) AS Err
FROM CharacterIdentityBuilds b
INNER JOIN CharacterIdentityAngles a ON a.BuildId = b.Id
LEFT JOIN CharacterIdentityAngleAttempts t ON t.AngleId = a.Id
WHERE b.CharacterProfileId = '{{id}}' AND b.TargetKind = 1
ORDER BY b.CreatedUtc DESC, a.View, t.AttemptNumber;
