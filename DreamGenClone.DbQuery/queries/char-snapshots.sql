SELECT CharacterSnapshotsJson, CharacterLocationsJson, CharacterLocationPerceptionsJson, UpdatedUtc
FROM RolePlayV2AdaptiveStates
WHERE SessionId = '{{id}}';