-- E2E non-destructiveness check: dump the stored ContinuationOverride fields for a session.
-- Promoted from artifacts/tmp/dbquery/queries/e2e_override_restore_check.sql (2026-09-02).
-- Usage: powershell -File helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/e2e-override-restore.sql <full-guid>
SELECT Id,
  Name,
  json_extract(json_extract(PayloadJson, '$.continuationOverride'), '$.tempo')       AS tempo,
  json_extract(json_extract(PayloadJson, '$.continuationOverride'), '$.span')        AS span,
  json_extract(json_extract(PayloadJson, '$.continuationOverride'), '$.pacing')      AS pacing,
  json_extract(json_extract(PayloadJson, '$.continuationOverride'), '$.beatScope')   AS beatScope,
  json_extract(json_extract(PayloadJson, '$.continuationOverride'), '$.wordTargetMin') AS wordMin,
  json_extract(json_extract(PayloadJson, '$.continuationOverride'), '$.wordTargetMax') AS wordMax
FROM Sessions
WHERE Id = '{{id}}';
