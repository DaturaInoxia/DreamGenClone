-- Every durable job type with its states, newest first: what is still non-terminal right now (and therefore what
-- the UI can still be reporting as "work in flight").
SELECT JobType, Status, COUNT(*) AS Jobs, MIN(CreatedUtc) AS Oldest, MAX(CreatedUtc) AS Newest,
       substr(COALESCE(MAX(ErrorMessage), ''), 1, 50) AS LastError
FROM DurableBackgroundJobs
GROUP BY JobType, Status
ORDER BY Newest DESC
LIMIT 40;
