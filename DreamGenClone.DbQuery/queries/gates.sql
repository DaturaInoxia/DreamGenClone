SELECT CreatedUtc, InteractionId, EventKind, Severity, Summary, MetadataJson
FROM RolePlayDebugEvents
WHERE SessionId = '{{id}}' AND (EventKind LIKE '%Gate%' OR Summary LIKE '%gate%')
ORDER BY CreatedUtc;