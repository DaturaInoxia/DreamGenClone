SELECT
    i.Id,
    i.InteractionId,
    i.Status,
    i.BeatId,
    i.Pov,
    i.CreatedUtc,
    i.PromptRecordId,
    i.PromptSnapshot,
    i.FileRelativePath
FROM SceneImages i
WHERE i.SessionId = '{{id}}'
ORDER BY i.CreatedUtc DESC;
