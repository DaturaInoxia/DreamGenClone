UPDATE DurableBackgroundJobs
SET Status = 'Cancelled',
    LeaseOwner = NULL,
    LeaseExpiresUtc = NULL,
    NextAttemptUtc = NULL,
    UpdatedUtc = strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
    CompletedUtc = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
WHERE Id = '05fae8f89c21434c959f3bd8fe008ebd'
  AND Status = 'Staged';