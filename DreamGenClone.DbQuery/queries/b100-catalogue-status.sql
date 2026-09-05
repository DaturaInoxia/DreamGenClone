SELECT
    c.Id AS CatalogueId,
    c.TurnId,
    c.Version,
    c.Status AS CatalogueStatus,
    c.CurrentAttemptId,
    c.CreatedUtc AS CatalogueCreatedUtc,
    c.StartedUtc AS CatalogueStartedUtc,
    c.CompletedUtc AS CatalogueCompletedUtc,
    c.UpdatedUtc AS CatalogueUpdatedUtc,
    a.Id AS AttemptId,
    a.AttemptNumber,
    a.JobId,
    a.Status AS AttemptStatus,
    a.CreatedUtc AS AttemptCreatedUtc,
    a.StartedUtc AS AttemptStartedUtc,
    a.CompletedUtc AS AttemptCompletedUtc,
    a.UpdatedUtc AS AttemptUpdatedUtc,
    a.ValidationCode,
    a.DurationMs,
    length(a.RawModelResponse) AS RawResponseLength,
    j.Status AS DurableJobStatus,
    j.AttemptCount AS DurableJobAttemptCount,
    j.MaxAttempts AS DurableJobMaxAttempts,
    j.LeaseOwner,
    j.LeaseExpiresUtc,
    j.NextAttemptUtc,
    j.ErrorCode AS DurableJobErrorCode,
    j.ErrorMessage AS DurableJobErrorMessage,
    j.UpdatedUtc AS DurableJobUpdatedUtc,
    c.ErrorCode,
    c.ErrorMessage
FROM SceneBeatCatalogues c
LEFT JOIN SceneBeatAnalysisAttempts a ON a.Id = c.CurrentAttemptId
LEFT JOIN DurableBackgroundJobs j ON j.Id = a.JobId
WHERE c.SessionId = '{{id}}'
ORDER BY c.Version DESC;