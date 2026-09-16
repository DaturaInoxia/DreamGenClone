SELECT
    'job' AS Kind,
    j.Id,
    j.JobType,
    j.Lane,
    j.Status,
    j.AttemptCount,
    j.MaxAttempts,
    j.NextAttemptUtc,
    j.LeaseOwner,
    j.LeaseExpiresUtc,
    j.ErrorCode,
    j.ErrorMessage,
    j.CreatedUtc,
    j.UpdatedUtc,
    j.CompletedUtc,
    j.PayloadJson
FROM DurableBackgroundJobs j
WHERE j.PayloadJson LIKE '%' || '{{id}}' || '%'
   OR j.DedupeKey LIKE '%' || '{{id}}' || '%'
UNION ALL
SELECT
    'image',
    i.Id,
    NULL,
    NULL,
    i.Status,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    i.ErrorMessage,
    i.CreatedUtc,
    i.UpdatedUtc,
    i.CompletedUtc,
    i.PromptRecordId
FROM SceneImages i
WHERE i.SessionId = '{{id}}'
  AND i.Status <> 'Complete'
ORDER BY UpdatedUtc DESC;