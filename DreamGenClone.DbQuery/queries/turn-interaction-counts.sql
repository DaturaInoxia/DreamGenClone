SELECT TurnIndex, TurnKind, Status, OutputInteractionCount, StartedUtc, CompletedUtc
FROM RolePlayV2Turns
WHERE SessionId = '{{id}}'
ORDER BY TurnIndex;