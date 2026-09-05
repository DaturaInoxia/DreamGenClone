SELECT 'enrichment' AS Kind, Id, Status, MomentId, Revision, NULL AS ContextId
FROM SceneMomentEnrichments
WHERE CatalogueId IN (SELECT CatalogueId FROM SceneImageProductionGroups WHERE SessionId = '{{id}}')
UNION ALL
SELECT 'group' AS Kind, Id, Status, MomentId, MomentEnrichmentRevision AS Revision, NULL AS ContextId
FROM SceneImageProductionGroups
WHERE SessionId = '{{id}}'
UNION ALL
SELECT 'workload' AS Kind, Id, Status, NULL AS MomentId, Revision, ContextId
FROM ProductionWorkloads
WHERE SessionId = '{{id}}'
ORDER BY Kind, Revision;
