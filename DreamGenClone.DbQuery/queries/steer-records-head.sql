SELECT Id, Succeeded, ErrorMessage,
       substr(GenerationResponse, 1, 600) AS RespHead,
       substr(ParsedOptionsJson, 1, 400) AS ParsedHead,
       CreatedUtc
FROM SteeringGenerationRecords
WHERE SessionId = '{{id}}'
ORDER BY CreatedUtc DESC
LIMIT 5;
