SELECT CurrentPhase, InteractionCountInPhase, TurnsInCurrentBeat, CurrentBeatCode, UpdatedUtc
FROM RolePlayV2AdaptiveStates
WHERE SessionId = '{{id}}';