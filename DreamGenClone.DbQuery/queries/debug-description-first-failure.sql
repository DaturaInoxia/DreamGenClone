-- The exact failure text of the FIRST failing description job (2026-09-25T01:50:58): it is either a provider HTTP
-- failure or an identity mismatch, and the two point at different fixes.
SELECT Id, Status, AttemptCount, ErrorCode, ErrorMessage, CreatedUtc, CompletedUtc
FROM DurableBackgroundJobs
WHERE JobType = 'scene-asset-image-edit-description' AND CreatedUtc = '2026-09-25T01:50:58.0831426Z';
