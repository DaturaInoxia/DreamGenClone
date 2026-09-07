SELECT
    j.Id AS JobId,
    j.JobType,
    j.Lane,
    j.Status AS JobStatus,
    j.AttemptCount,
    j.MaxAttempts,
    j.CreatedUtc AS JobCreatedUtc,
    j.UpdatedUtc AS JobUpdatedUtc,
    j.LeaseOwner,
    j.LeaseExpiresUtc,
    j.NextAttemptUtc,
    j.ErrorCode AS JobErrorCode,
    j.ErrorMessage AS JobErrorMessage,
    j.DedupeKey,
    j.PayloadJson,
    i.Id AS ImageId,
    i.Status AS ImageStatus,
    i.Operation,
    i.ProductionStage,
    i.SourceImageId,
    i.EditSessionId,
    i.EditCompilationAttemptId,
    i.EditPromptRevisionId,
    i.ModelIdentifier AS ImageModelIdentifier,
    i.ProviderName AS ImageProviderName,
    i.ErrorMessage AS ImageErrorMessage,
    i.CreatedUtc AS ImageCreatedUtc,
    i.UpdatedUtc AS ImageUpdatedUtc,
    s.Status AS EditSessionStatus,
    s.SourceImageSha256,
    a.Status AS CompilationAttemptStatus,
    a.ResolvedModelSnapshotJson,
    a.Error AS CompilationError,
    a.CreatedUtc AS CompilationCreatedUtc,
    a.CompletedUtc AS CompilationCompletedUtc,
    r.RevisionKind,
    r.Prompt AS CompiledPrompt,
    r.CreatedUtc AS RevisionCreatedUtc
FROM DurableBackgroundJobs j
LEFT JOIN SceneImages i ON i.Id = json_extract(j.PayloadJson, '$.ImageRecordId')
LEFT JOIN SceneImageEditSessions s ON s.Id = i.EditSessionId
LEFT JOIN SceneImageEditCompilationAttempts a ON a.Id = i.EditCompilationAttemptId
LEFT JOIN SceneImageEditPromptRevisions r ON r.Id = i.EditPromptRevisionId
WHERE j.DedupeKey = 'scene-image-editing:1bfaba9a-ca82-458d-adec-dcbbc2aac3ba'
ORDER BY j.UpdatedUtc DESC;