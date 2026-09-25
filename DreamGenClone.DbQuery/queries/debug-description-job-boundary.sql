-- The description jobs around the boundary: successes and failures interleaved show WHEN the provider started
-- echoing a different model identity (the readiness probe keeps succeeding, the real completion does not).
SELECT CreatedUtc, Status, AttemptCount,
       substr(COALESCE(ErrorMessage, ''), 1, 45) AS Error
FROM DurableBackgroundJobs
WHERE JobType = 'scene-asset-image-edit-description' AND CreatedUtc >= '2026-09-25T01:20'
ORDER BY CreatedUtc
LIMIT 30;
