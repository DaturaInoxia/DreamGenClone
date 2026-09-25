-- The full failure text of the newest asset-image edit description jobs: this is what leaves a workspace session
-- with no description, which is what keeps "Prepare edit" disabled.
SELECT CreatedUtc, AttemptCount, MaxAttempts, ErrorCode, ErrorMessage
FROM DurableBackgroundJobs
WHERE JobType = 'scene-asset-image-edit-description' AND Status = 'Failed'
ORDER BY CreatedUtc DESC
LIMIT 3;
