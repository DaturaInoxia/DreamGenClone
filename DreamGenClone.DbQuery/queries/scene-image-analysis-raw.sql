SELECT
    Id,
    AnchorInteractionId,
    Status,
    ErrorMessage,
    RawModelResponse,
    ReasoningContent
FROM SceneImageBeatAnalyses
WHERE SessionId = '{{id}}'
  AND Status = 'Failed'
ORDER BY UpdatedUtc DESC;
