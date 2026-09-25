-- The approved pack's face assets with their checksums: comparing these with the image-generator harness refs shows
-- whether the harness still carries the previous identity.
SELECT FaceView, Id, IsApproved, ByteLength, Sha256, FileRelativePath, SourceLabel
FROM SceneImageReferenceAssets
WHERE IdentityPackId = '2d13c667-a690-4b5a-992a-93359591aa41' AND AssetKind = 'Face'
ORDER BY FaceView;
