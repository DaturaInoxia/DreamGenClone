SELECT 'images' AS Kind, Status, COUNT(*) AS Count, MIN(CreatedUtc) AS Oldest, MAX(UpdatedUtc) AS Newest
FROM SceneImages
WHERE SessionId = '{{id}}'
GROUP BY Status
UNION ALL
SELECT 'jobs' AS Kind, Status, COUNT(*) AS Count, MIN(CreatedUtc), MAX(UpdatedUtc)
FROM DurableBackgroundJobs
WHERE PayloadJson LIKE '%' || '{{id}}' || '%'
GROUP BY Status;

SELECT i.Id AS ImageId, i.Status AS ImageStatus, i.CreatedUtc, i.UpdatedUtc,
       j.Id AS JobId, j.Status AS JobStatus, j.JobType, j.AttemptCount, j.MaxAttempts,
       j.ErrorCode, j.ErrorMessage, j.UpdatedUtc AS JobUpdatedUtc
FROM SceneImages i
LEFT JOIN DurableBackgroundJobs j ON j.PayloadJson LIKE '%' || i.Id || '%'
WHERE i.SessionId = '{{id}}'
  AND i.Status <> 'Complete'
ORDER BY i.UpdatedUtc DESC;