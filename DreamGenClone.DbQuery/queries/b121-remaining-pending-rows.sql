-- The one remaining Pending row (not an edit): reported for completeness after the 066 repair.
SELECT Id, SessionId, BeatId, ProductionStage, Operation, Status, SourceImageId, CreatedUtc, UpdatedUtc
FROM SceneImages
WHERE Status = 'Pending'
ORDER BY CreatedUtc DESC;
