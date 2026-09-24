-- Inspect a single durable background job (identity edit/reference generation) by job id.
SELECT Id AS JobId, JobType, Lane, Status AS JobStatus, AttemptCount, MaxAttempts,
       NextAttemptUtc, LeaseOwner, LeaseExpiresUtc, ErrorCode, ErrorMessage,
       DedupeKey, CreatedUtc, UpdatedUtc, CompletedUtc, PayloadJson
FROM DurableBackgroundJobs
WHERE Id = '{{id}}';
