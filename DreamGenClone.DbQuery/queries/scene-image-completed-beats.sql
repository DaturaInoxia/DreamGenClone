SELECT
    Id,
    TurnId,
    AnchorInteractionId,
    UpdatedUtc,
    BeatsJson
FROM SceneImageBeatAnalyses
WHERE SessionId = '{{id}}'
  AND Status = 'Complete'
ORDER BY UpdatedUtc DESC;
