SELECT p.Id AS PlanId,
       p.CatalogueId,
       p.BeatId,
       p.CatalogueVersion,
       p.Version,
       p.Status AS PlanStatus,
       p.CurrentAttemptId,
       p.ModelIdentifier,
       p.ProviderName,
       p.CreatedUtc,
       p.StartedUtc,
       p.CompletedUtc,
       p.UpdatedUtc,
       a.AttemptNumber,
       a.Status AS AttemptStatus,
       a.JobId,
       a.StartedUtc AS AttemptStartedUtc,
       a.CompletedUtc AS AttemptCompletedUtc,
       a.ValidationCode,
       a.DurationMs,
       j.Status AS DurableJobStatus,
       j.AttemptCount AS DurableJobAttemptCount,
       j.MaxAttempts AS DurableJobMaxAttempts,
       j.LeaseOwner,
       j.LeaseExpiresUtc,
       j.NextAttemptUtc,
       j.ErrorCode AS DurableJobErrorCode,
       j.ErrorMessage AS DurableJobErrorMessage,
       j.UpdatedUtc AS DurableJobUpdatedUtc
FROM SceneBeatProductionPlans p
JOIN SceneBeatCatalogues c ON c.Id = p.CatalogueId
LEFT JOIN SceneBeatProductionAttempts a ON a.Id = p.CurrentAttemptId
LEFT JOIN DurableBackgroundJobs j ON j.Id = a.JobId
WHERE c.SessionId LIKE '{{id}}%'
ORDER BY p.CreatedUtc DESC, p.BeatId;
