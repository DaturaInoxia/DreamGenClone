SELECT Id, SessionId, ContextId, Revision, Status, PayloadJson
FROM ProductionWorkloads
WHERE SessionId = '{{id}}'
ORDER BY CreatedUtc DESC;
