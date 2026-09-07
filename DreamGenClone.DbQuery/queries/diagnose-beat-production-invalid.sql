SELECT
    j.Id AS JobId,
    j.Status AS JobStatus,
    j.ErrorCode AS JobErrorCode,
    j.ErrorMessage AS JobErrorMessage,
    a.OwnerRecordId AS PlanId,
    a.Id AS AttemptId,
    p.BeatId,
    json_extract(p.SourceSnapshotJson, '$.Turn.SessionId') AS SessionId,
    json_extract(p.SourceSnapshotJson, '$.Turn.InteractionId') AS InteractionId,
    p.Status AS PlanStatus,
    p.ErrorCode AS PlanErrorCode,
    p.ErrorMessage AS PlanErrorMessage,
    a.Status AS AttemptStatus,
    a.AttemptNumber,
    a.ValidationCode,
    a.ValidationDetailsJson,
    (SELECT group_concat(json_extract(value, '$.order'), ',') FROM json_each(a.RawModelResponse, '$.events')) AS EventOrders,
    (SELECT group_concat(json_extract(value, '$.order'), ',') FROM json_each(a.RawModelResponse, '$.narration')) AS NarrationOrders,
    (SELECT group_concat(json_extract(value, '$.order'), ',') FROM json_each(a.RawModelResponse, '$.dialogue')) AS DialogueOrders,
    (SELECT group_concat(json_extract(value, '$.order'), ',') FROM json_each(a.RawModelResponse, '$.soundEvents')) AS SoundOrders,
    (SELECT group_concat(json_extract(value, '$.order'), ',') FROM json_each(a.RawModelResponse, '$.music')) AS MusicOrders,
    (SELECT group_concat(json_extract(value, '$.order'), ',') FROM json_each(a.RawModelResponse, '$.actionArc')) AS ActionOrders,
    a.FinishReason,
    a.CreatedUtc AS AttemptCreatedUtc,
    a.CompletedUtc AS AttemptCompletedUtc
FROM SceneBeatProductionAttempts a
LEFT JOIN SceneBeatProductionPlans p
    ON p.Id = a.OwnerRecordId
LEFT JOIN DurableBackgroundJobs j
    ON j.PayloadJson LIKE '%' || a.Id || '%'
WHERE a.ValidationCode = 'scene_beat_production_output_invalid'
    AND (p.SourceSnapshotJson LIKE '%bda4f5c0-ce31-4a67-800f-109892d0e0d0%'
             OR a.OwnerRecordId = 'e97e3219-390a-436b-b128-5490dad437f9')
ORDER BY j.CreatedUtc DESC;