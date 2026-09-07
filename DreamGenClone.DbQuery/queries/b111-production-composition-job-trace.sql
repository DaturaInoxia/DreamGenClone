WITH groups AS (
    SELECT Id, Pov, Status, MomentEnrichmentId, CreatedUtc, UpdatedUtc
    FROM SceneImageProductionGroups
    WHERE SessionId = '{{id}}'
      AND InteractionId = 'dad139a5-8e98-469b-ab74-8e55404f1732'
      AND MomentEnrichmentId = 'e2a8838a-bd13-4f7a-bf2c-df6cd728ee78'
), attempt_ids AS (
    SELECT Id, ProductionGroupId, PromptRecordId, ProductionStage, Status, ErrorMessage, CreatedUtc, UpdatedUtc
    FROM SceneImages
    WHERE ProductionGroupId IN (SELECT Id FROM groups)
), prompt_ids AS (
    SELECT Id, ProductionGroupId, Status, ErrorMessage, CreatedUtc, UpdatedUtc
    FROM SceneImagePrompts
    WHERE ProductionGroupId IN (SELECT Id FROM groups)
)
SELECT
    'group' AS RecordKind,
    Id,
    Pov AS Detail,
    Status,
    NULL AS JobType,
    NULL AS Lane,
    NULL AS AttemptCount,
    NULL AS MaxAttempts,
    NULL AS NextAttemptUtc,
    NULL AS LeaseOwner,
    NULL AS LeaseExpiresUtc,
    NULL AS ErrorCode,
    NULL AS ErrorMessage,
    CreatedUtc,
    UpdatedUtc,
    NULL AS CompletedUtc,
    NULL AS PayloadJson
FROM groups
UNION ALL
SELECT
    'image' AS RecordKind,
    Id,
    ProductionGroupId || ' / ' || ProductionStage AS Detail,
    Status,
    NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, ErrorMessage, CreatedUtc, UpdatedUtc, NULL, PromptRecordId
FROM attempt_ids
UNION ALL
SELECT
    'prompt' AS RecordKind,
    Id,
    ProductionGroupId AS Detail,
    Status,
    NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, ErrorMessage, CreatedUtc, UpdatedUtc, NULL, NULL
FROM prompt_ids
UNION ALL
SELECT
    'job' AS RecordKind,
    job.Id,
    job.DedupeKey AS Detail,
    job.Status,
    job.JobType,
    job.Lane,
    job.AttemptCount,
    job.MaxAttempts,
    job.NextAttemptUtc,
    job.LeaseOwner,
    job.LeaseExpiresUtc,
    job.ErrorCode,
    job.ErrorMessage,
    job.CreatedUtc,
    job.UpdatedUtc,
    job.CompletedUtc,
    job.PayloadJson
FROM DurableBackgroundJobs job
WHERE EXISTS (
    SELECT 1
    FROM attempt_ids image
    WHERE job.PayloadJson LIKE '%' || image.Id || '%'
)
   OR EXISTS (
    SELECT 1
    FROM prompt_ids prompt
    WHERE job.PayloadJson LIKE '%' || prompt.Id || '%'
)
ORDER BY UpdatedUtc DESC, RecordKind, Id;