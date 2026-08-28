SELECT
    Id,
    SessionId,
    TurnId,
    AnchorInteractionId,
    Status,
    ModelIdentifier,
    substr(ErrorMessage, 1, 300) AS ErrorMessage,
    length(RawModelResponse) AS RawModelResponseLen,
    length(ReasoningContent) AS ReasoningContentLen,
    length(BeatsJson) AS BeatsJsonLen,
    CreatedUtc,
    UpdatedUtc
FROM SceneImageBeatAnalyses
WHERE SessionId = '{{id}}'
ORDER BY CreatedUtc DESC;
