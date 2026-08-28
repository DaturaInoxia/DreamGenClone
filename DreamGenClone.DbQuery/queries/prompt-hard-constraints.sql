SELECT CreatedUtc, InteractionId, EventKind, Summary, MetadataJson
FROM RolePlayDebugEvents
WHERE SessionId = '{{id}}' AND MetadataJson LIKE '%HARD CONSTRAINT%'
ORDER BY CreatedUtc;