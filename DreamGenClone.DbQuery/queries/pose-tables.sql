SELECT name FROM sqlite_master WHERE type='table' AND (name LIKE '%Pose%' OR name LIKE '%Skeleton%') ORDER BY name;
