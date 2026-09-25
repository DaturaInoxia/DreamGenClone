-- B-123 read-back: the state the LoRA cell workspace depends on, read straight from the live dev DB.
--
-- Companion to b123-lora-cell-templates.sql (which dumps the wording). This one answers the four
-- questions that decide whether the tab can be used at all:
--   1. are the render cells seeded (12 of them, one per view-and-distance),
--   2. is the deleted `lora.cell.negative` row confirmed ABSENT (removed by operator instruction),
--   3. is the curation policy row present (the plan generator and the gates resolve through it),
--   4. does any LoRA dataset exist yet, and does it carry a coverage plan / container.
--
-- Everything here is read-only. The rows are written on app startup, so an app that predates a seed
-- change shows fewer rows and the fix is a restart, not an inserted row.
SELECT 'render-cells' AS What, COUNT(*) AS Rows, NULL AS Detail
FROM ImageWorkflowPromptTemplates
WHERE Key LIKE 'lora.cell.render.%'
UNION ALL
SELECT 'caption-templates', COUNT(*), NULL
FROM ImageWorkflowPromptTemplates
WHERE Key = 'lora.cell.caption'
UNION ALL
SELECT 'negative-row-must-be-zero', COUNT(*), NULL
FROM ImageWorkflowPromptTemplates
WHERE Key = 'lora.cell.negative'
UNION ALL
SELECT 'vocabulary-rows', COUNT(*), NULL
FROM ImageWorkflowPromptTemplates
WHERE Key LIKE 'lora.vocabulary.%'
UNION ALL
SELECT 'curation-policies', COUNT(*), NULL
FROM CharacterLoraCurationPolicies
UNION ALL
SELECT 'curation-policy-rows', COUNT(*),
       CHAR(10) || group_concat(
           COALESCE(CharacterProfileId, 'global') || ' · chars=' || length(PayloadJson)
           || ' · seedChars=' || length(SeedPayloadJson),
           CHAR(10))
FROM CharacterLoraCurationPolicies
UNION ALL
SELECT 'lora-datasets', COUNT(*), NULL
FROM CharacterLoraDatasets
UNION ALL
SELECT 'lora-dataset-members', COUNT(*), NULL
FROM CharacterLoraDatasetMembers
UNION ALL
-- Payloads are written with JsonSerializerDefaults.Web, so the paths are camelCase.
SELECT 'datasets-with-plan', COUNT(*),
       CHAR(10) || group_concat(
           Id || ' · ' || CharacterProfileId || ' · status=' || Status
           || ' · v' || Version || ' · family=' || TargetModelFamily
           || ' · pack=' || IdentityPackId
           || ' · planChars=' || COALESCE(length(json_extract(PayloadJson, '$.coveragePlanJson')), 0)
           || ' · policyChars=' || COALESCE(length(json_extract(PayloadJson, '$.curationPolicyJson')), 0)
           || ' · container=' || COALESCE(json_extract(PayloadJson, '$.containerAssetId'), 'none'),
           CHAR(10))
FROM CharacterLoraDatasets
UNION ALL
-- The dataset's CharacterProfileId is a character TEMPLATE id (the owner resolver keys identity by template),
-- so the name comes from Templates, not from the roleplay CharacterProfiles table.
SELECT 'dataset-owner', COUNT(*), CHAR(10) || group_concat(c.Id || ' · ' || c.Name, CHAR(10))
FROM CharacterLoraDatasets d
JOIN Templates c ON c.Id = d.CharacterProfileId AND c.TemplateType = 'Character'
UNION ALL
SELECT 'cell-attempt-images', COUNT(*), NULL
FROM SceneAssetImages
WHERE CandidateBatchId LIKE 'lora-cell-%'
UNION ALL
SELECT 'cell-attempt-batches', COUNT(*), CHAR(10) || group_concat(Batch, CHAR(10))
FROM (SELECT CandidateBatchId || ' · images=' || COUNT(*) AS Batch
      FROM SceneAssetImages WHERE CandidateBatchId LIKE 'lora-cell-%'
      GROUP BY CandidateBatchId)
UNION ALL
SELECT 'image-level-approvals', COUNT(*), NULL
FROM SceneAssetImages
WHERE ProductionApprovalStatus = 'Approved'
UNION ALL
SELECT 'asset-level-approvals', COUNT(*), NULL
FROM SceneAssets
WHERE ProductionApprovalStatus = 'Approved';
