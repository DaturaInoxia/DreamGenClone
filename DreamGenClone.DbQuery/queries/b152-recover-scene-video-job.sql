UPDATE DurableBackgroundJobs
SET Status = 'Complete',
    ErrorCode = NULL,
    ErrorMessage = NULL,
    CompletedUtc = '2026-10-07T16:55:30.0000000Z',
    UpdatedUtc = '2026-10-07T17:35:21.5279579Z',
    LeaseOwner = NULL,
    LeaseExpiresUtc = NULL
WHERE Id = '32eb98eeaf2c4c6c94e7fd63825bfa2e'
  AND Status = 'Failed';
