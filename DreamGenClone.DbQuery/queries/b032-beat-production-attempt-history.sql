SELECT p.Id AS PlanId,
       p.BeatId,
       p.Status AS PlanStatus,
       p.CurrentAttemptId,
       a.Id AS AttemptId,
       a.AttemptNumber,
       a.JobId,
       a.Status AS AttemptStatus,
       a.ValidationCode,
       a.ValidationDetailsJson,
       a.DurationMs,
       a.InputCharacters,
       a.OutputCharacters,
       a.CreatedUtc,
       a.StartedUtc,
       a.CompletedUtc,
       a.UpdatedUtc,
       j.Status AS DurableJobStatus,
       j.AttemptCount AS DurableJobAttemptCount,
       j.MaxAttempts AS DurableJobMaxAttempts,
       j.ErrorCode AS DurableJobErrorCode,
       j.ErrorMessage AS DurableJobErrorMessage
FROM SceneBeatProductionPlans p
JOIN SceneBeatCatalogues c ON c.Id = p.CatalogueId
JOIN SceneBeatProductionAttempts a ON a.OwnerRecordId = p.Id
LEFT JOIN DurableBackgroundJobs j ON j.Id = a.JobId
WHERE c.SessionId LIKE '{{id}}%'
ORDER BY p.CreatedUtc DESC, p.BeatId, a.AttemptNumber;
