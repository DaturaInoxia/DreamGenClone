-- The body views: status, artifact, override flag and the recorded verdicts (what the acceptance gate reads).
-- {{id}} is the buildId.
SELECT v.BuildId, v.State, v.BodyView, v.RotationDeg, v.PositionKey, v.Status, v.OutputArtifactId,
       v.ShapeVerdict, v.TattooVerdict, v.SkinVerdict, v.AnatomyVerdict, v.ReviewedBy,
       v.ManualOverrideApplied, v.UpdatedUtc
FROM CharacterIdentityBodyViews v
WHERE v.BuildId = '{{id}}'
ORDER BY v.State, v.BodyView;
