SELECT
    Id,
    BeatId,
    Pov,
    Status,
    CreatedUtc,
    PromptRecordId,
    PromptSnapshot,
    FileRelativePath
FROM SceneImages
WHERE InteractionId = '{{id}}'
ORDER BY CreatedUtc DESC;
