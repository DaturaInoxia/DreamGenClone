SELECT l.Name AS Library, l.IsSystem, l.Description, COUNT(p.Id) AS Presets
FROM PoseLibraries l LEFT JOIN PosePresets p ON p.LibraryId = l.Id
GROUP BY l.Id ORDER BY l.Name;
