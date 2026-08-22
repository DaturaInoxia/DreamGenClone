SELECT SemanticStatDeltaBreakdownsJson, SemanticDeltaBreakdownsJson, UpdatedUtc
FROM RolePlayV2AdaptiveStates
WHERE SessionId = '{{id}}';