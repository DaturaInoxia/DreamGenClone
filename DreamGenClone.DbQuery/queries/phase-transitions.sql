SELECT *
FROM RolePlayV2PhaseTransitions
WHERE SessionId = '{{id}}'
ORDER BY OccurredUtc;