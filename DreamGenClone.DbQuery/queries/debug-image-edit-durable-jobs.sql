-- The durable jobs behind the image-edit workspace: the description job a workspace load enqueues and the prompt
-- compilation job "Prepare edit" enqueues. A row that is not terminal is why the panel reports work in flight.
SELECT JobType, Status, AttemptCount, MaxAttempts, ErrorCode,
       substr(COALESCE(ErrorMessage, ''), 1, 60) AS Error, CreatedUtc, NextAttemptUtc, CompletedUtc
FROM DurableBackgroundJobs
WHERE JobType LIKE '%SceneAssetImageEdit%'
ORDER BY CreatedUtc DESC
LIMIT 25;
