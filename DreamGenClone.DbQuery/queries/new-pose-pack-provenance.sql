SELECT p.Name, p.LibraryId, p.KnownGood, p.SkeletonPngPath, p.ProvenanceJson
FROM PosePresets p
JOIN PoseLibraries l ON l.Id = p.LibraryId
WHERE l.Name = 'OpenPoses Collection' AND p.Category = 'tpose'
ORDER BY p.Name;
