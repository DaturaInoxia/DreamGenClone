-- Queue rows for the stuck Identity edit images.
SELECT Id AS JobId, JobType, Lane, Status, AttemptCount, MaxAttempts, ErrorCode, ErrorMessage,
       CreatedUtc, UpdatedUtc, CompletedUtc, DedupeKey
FROM DurableBackgroundJobs
WHERE JobType = 'media-edit-image-editing'
  AND (PayloadJson LIKE '%95a3cadd-3825-44ef-88f5-846bfaccf45d%'
    OR PayloadJson LIKE '%6bae2aa0-9086-4680-b1b8-736c32bdff97%'
    OR PayloadJson LIKE '%e335f53e-2b4b-4caf-b079-30c9e44639d5%')
ORDER BY CreatedUtc DESC;
