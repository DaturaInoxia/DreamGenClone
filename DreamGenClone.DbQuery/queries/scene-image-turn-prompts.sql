SELECT
    Id,
    BeatAnalysisId,
    json_extract(BeatSnapshotJson, '$.beatId') AS BeatId,
    Pov,
    Status,
    CreatedUtc,
    UpdatedUtc,
    OutputPrompt
FROM SceneImagePrompts
WHERE InteractionId = '{{id}}'
ORDER BY UpdatedUtc DESC;
