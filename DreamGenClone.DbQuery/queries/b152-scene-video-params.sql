SELECT Id, Title, Status, Length, Steps, Width, Height, Fps, Seed, ModelIdentifier,
       RequestedModelId, ProviderName, StartedUtc, CreatedUtc, UpdatedUtc
FROM SceneVideos
WHERE OriginImageId = '{{id}}' OR Id = '{{id}}'
ORDER BY CreatedUtc DESC
LIMIT 3;
