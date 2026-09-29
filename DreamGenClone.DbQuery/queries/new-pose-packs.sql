SELECT l.Name AS Library, p.Category, COUNT(*) AS Poses, MIN(p.Name) AS FirstName, MAX(p.Name) AS LastName
FROM PosePresets p
JOIN PoseLibraries l ON l.Id = p.LibraryId
WHERE l.Name IN ('OpenPose From Above - on knees', 'OpenPoses Collection')
GROUP BY l.Name, p.Category
ORDER BY l.Name, p.Category;
