SELECT CurrentPhase, ActiveScenarioId, CurrentBeatCode, UpdatedUtc
FROM RolePlayV2AdaptiveStates
WHERE SessionId = '{{id}}';