SELECT EventKind, Severity, Summary, MetadataJson, CreatedUtc
FROM RolePlayDebugEvents
WHERE SessionId LIKE '{{id}}%'
  AND InteractionId = '3c50e37b-8eca-4506-b408-c0e3105c4eba'
  AND CreatedUtc >= '2026-08-24T01:00:00Z'
ORDER BY CreatedUtc DESC
LIMIT 15;
