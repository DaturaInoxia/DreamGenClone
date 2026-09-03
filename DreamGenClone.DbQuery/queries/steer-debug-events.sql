SELECT Id, EventKind, Severity, ActorName, ModelIdentifier, DurationMs,
       Summary, CreatedUtc
FROM RolePlayDebugEvents
WHERE SessionId = '{{id}}'
  AND (EventKind LIKE '%Steer%' OR EventKind LIKE '%AutoSteer%' OR Summary LIKE '%steer%')
ORDER BY CreatedUtc DESC
LIMIT 40;
