SELECT
    jobs.Id AS JobId,
    jobs.JobType,
    jobs.Lane,
    jobs.Status AS JobStatus,
    jobs.AttemptCount,
    jobs.MaxAttempts,
    jobs.CreatedUtc AS JobCreatedUtc,
    jobs.UpdatedUtc AS JobUpdatedUtc,
    jobs.LeaseOwner,
    jobs.LeaseExpiresUtc,
    jobs.ErrorCode AS JobErrorCode,
    jobs.ErrorMessage AS JobErrorMessage,
    jobs.PayloadJson,
    json_extract(jobs.PayloadJson, '$.ImageRecordId') AS SceneImageId,
    images.Status AS ImageStatus,
    images.Operation AS ImageOperation,
    images.ProductionStage,
    images.StartedUtc AS ImageStartedUtc,
    images.CompletedUtc AS ImageCompletedUtc,
    images.RequestedModelId,
    images.ModelIdentifier AS ResolvedModelIdentifier,
    images.ProviderName AS ResolvedProviderName,
    images.ErrorMessage AS ImageErrorMessage,
    sessions.Status AS EditSessionStatus,
    sessions.UpdatedUtc AS EditSessionUpdatedUtc
FROM DurableBackgroundJobs AS jobs
LEFT JOIN SceneImages AS images
    ON images.Id = json_extract(jobs.PayloadJson, '$.ImageRecordId')
LEFT JOIN SceneImageEditSessions AS sessions
    ON sessions.Id = images.EditSessionId
WHERE jobs.Id LIKE '{{id}}%'
   OR jobs.DedupeKey = '{{id}}';