SELECT i.Id AS ImageId, i.Status AS ImageStatus, i.CreatedUtc, i.UpdatedUtc, i.PromptRecordId,
       j.Id AS JobId, j.Status AS JobStatus, j.JobType, j.AttemptCount, j.MaxAttempts,
       j.ErrorCode, j.ErrorMessage, j.CreatedUtc AS JobCreatedUtc, j.UpdatedUtc AS JobUpdatedUtc,
       j.PayloadJson
FROM SceneImages i
LEFT JOIN DurableBackgroundJobs j ON j.PayloadJson LIKE '%' || i.Id || '%'
WHERE i.SessionId = '{{id}}'
  AND i.Status IN ('Pending', 'Generating')
ORDER BY i.CreatedUtc;