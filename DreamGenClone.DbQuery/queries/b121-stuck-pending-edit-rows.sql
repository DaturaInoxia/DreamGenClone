-- The stuck Pending Edit rows (never claimed) and their durable jobs.
SELECT i.Id AS ImageId, i.SessionId, i.InteractionId, i.BeatId, i.ProductionStage, i.Status,
       i.SourceImageId, i.CreatedUtc, i.StartedUtc, i.UpdatedUtc,
       (SELECT COUNT(*) FROM DurableBackgroundJobs j
         WHERE j.PayloadJson LIKE '%' || i.Id || '%') AS JobCount
FROM SceneImages i
WHERE i.Operation = 'Edit' AND i.Status = 'Pending'
ORDER BY i.CreatedUtc DESC;
