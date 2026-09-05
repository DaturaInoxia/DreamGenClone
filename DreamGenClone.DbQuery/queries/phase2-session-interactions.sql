SELECT
    json_extract(value, '$.id') AS InteractionId,
    json_extract(value, '$.createdAt') AS CreatedUtc,
    json_extract(value, '$.actorName') AS ActorName,
    json_extract(value, '$.status') AS Status
FROM Sessions, json_each(json_extract(PayloadJson, '$.interactions'))
WHERE Sessions.Id = '{{id}}'
ORDER BY CreatedUtc DESC;
