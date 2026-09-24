-- Scene-image Edit rows that were never claimed (stuck Pending) and any job that references them.
SELECT 'PendingEditRows' AS Probe, COUNT(*) AS Rows
FROM SceneImages WHERE Operation = 'Edit' AND Status = 'Pending'
UNION ALL
SELECT 'PendingEditRows_StartedNull', COUNT(*)
FROM SceneImages WHERE Operation = 'Edit' AND Status = 'Pending' AND StartedUtc IS NULL
UNION ALL
SELECT 'PendingEditRows_StartIdentity', COUNT(*)
FROM SceneImages WHERE Operation = 'Edit' AND Status = 'Pending' AND ProductionStage = 'Identity'
UNION ALL
SELECT 'AllPendingRows', COUNT(*)
FROM SceneImages WHERE Status = 'Pending'
UNION ALL
SELECT 'PendingWithFileOnDiskClaimed', COUNT(*)
FROM SceneImages WHERE Status = 'Pending' AND Sha256 IS NOT NULL;
