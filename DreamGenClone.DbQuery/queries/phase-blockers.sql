SELECT CreatedUtc, EventKind, Severity, Summary, MetadataJson
FROM RolePlayDebugEvents
WHERE SessionId = '{{id}}' AND (EventKind LIKE '%Phase%' OR Summary LIKE '%phase%')
ORDER BY CreatedUtc;