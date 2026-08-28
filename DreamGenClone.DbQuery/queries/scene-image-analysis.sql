SELECT
    Id,
    SessionId,
    TurnId,
    AnchorInteractionId,
    Status,
    ModelIdentifier,
    ErrorMessage,
    CreatedUtc,
    UpdatedUtc,
    length(RawModelResponse) AS RawModelResponseLength,
    length(ReasoningContent) AS ReasoningContentLength,
    length(BeatsJson) AS BeatsJsonLength
FROM SceneImageBeatAnalyses
WHERE SessionId = '{{id}}'
ORDER BY UpdatedUtc DESC;
