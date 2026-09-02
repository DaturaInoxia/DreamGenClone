SELECT
    Id,
    SessionId,
    InteractionId,
    Status,
    ModelIdentifier,
    BeatAnalysisId,
    Pov,
    ErrorMessage,
    CreatedUtc,
    UpdatedUtc,
    length(InputExcerpt) AS InputExcerptLength,
    length(OutputPrompt) AS OutputPromptLength,
    length(BeatSnapshotJson) AS BeatSnapshotLength,
    OutputPrompt
FROM SceneImagePrompts
WHERE SessionId = '{{id}}'
ORDER BY UpdatedUtc DESC;
