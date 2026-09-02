SELECT CreatedUtc, InteractionId, EventKind, Severity, Summary, MetadataJson
FROM RolePlayDebugEvents
WHERE SessionId = '{{id}}' AND EventKind LIKE '%Semantic%'
ORDER BY CreatedUtc;