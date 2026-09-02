SELECT 'legacy_analysis_total' AS Metric, COUNT(*) AS Value
FROM SceneImageBeatAnalyses
UNION ALL
SELECT 'legacy_analysis_complete', COUNT(*)
FROM SceneImageBeatAnalyses
WHERE Status = 'Complete'
UNION ALL
SELECT 'legacy_analysis_with_raw_response', COUNT(*)
FROM SceneImageBeatAnalyses
WHERE RawModelResponse IS NOT NULL OR ReasoningContent IS NOT NULL
UNION ALL
SELECT 'legacy_jobs_active', COUNT(*)
FROM DurableBackgroundJobs
WHERE JobType = 'scene-image-beat-generation'
  AND Status IN ('Pending', 'Processing', 'RetryScheduled');

SELECT Id, Status, AttemptCount, CreatedUtc, UpdatedUtc
FROM DurableBackgroundJobs
WHERE JobType = 'scene-image-beat-generation'
ORDER BY CreatedUtc DESC;
