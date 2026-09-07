SELECT
    sessions.Id AS EditSessionId,
    sessions.SourceImageId,
    sessions.Status AS SessionStatus,
    attempts.Id AS AttemptId,
    attempts.Status AS AttemptStatus,
    attempts.RawIntent,
    attempts.Error AS AttemptError,
    revisions.Ordinal AS RevisionOrdinal,
    revisions.Prompt,
    sessions.UpdatedUtc
FROM SceneImageEditSessions AS sessions
LEFT JOIN SceneImageEditCompilationAttempts AS attempts
    ON attempts.EditSessionId = sessions.Id
LEFT JOIN SceneImageEditPromptRevisions AS revisions
    ON revisions.CompilationAttemptId = attempts.Id
ORDER BY sessions.UpdatedUtc DESC, attempts.Ordinal DESC, revisions.Ordinal DESC
LIMIT 30;