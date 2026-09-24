-- Completed Identity/Finish edit rows in the same session: the exact model/provider/policy an edit of
-- this session's resolved editor model records, so the repair copies real values instead of inventing them.
SELECT Id, BeatId, ProductionStage, Operation, Status, ModelIdentifier, ProviderName, ContentPolicy,
       ImageSize, FileRelativePath, Sha256, CreatedUtc, StartedUtc, CompletedUtc
FROM SceneImages
WHERE SessionId = '8bc36efb-b235-485b-8675-b98ad7754e59'
ORDER BY CreatedUtc DESC
LIMIT 40;
