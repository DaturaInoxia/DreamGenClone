-- The compilation attempts of every session opened on the three-quarter view's SOURCE image (the canonical front):
-- an attempt left Pending/Running is what makes the edit workspace report "work in flight" and keeps
-- "Prepare edit" disabled.
SELECT a.Id AS AttemptId, s.Id AS SessionId, s.Status AS SessionStatus, a.Ordinal,
       a.Status AS AttemptStatus, a.CreatedUtc, a.StartedUtc, a.CompletedUtc,
       substr(COALESCE(a.Error, ''), 1, 50) AS Err
FROM SceneAssetImageEditCompilationAttempts a
INNER JOIN SceneAssetImageEditSessions s ON s.Id = a.EditSessionId
WHERE s.SourceImageId = '3b8f7dd9d0d84c6cb53f371b2b8df39e'
ORDER BY s.CreatedUtc DESC, a.Ordinal
LIMIT 25;
