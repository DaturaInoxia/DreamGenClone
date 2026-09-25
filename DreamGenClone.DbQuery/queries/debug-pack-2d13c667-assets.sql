-- The assets the promotion recorded in the pack the operator was told about: one row per view per upload, since
-- UploadAssetAsync always inserts a new asset id. Duplicates here mean a second promotion ADDED slots instead of
-- replacing them; none here means the promotion did not upload at all.
SELECT FaceView, AssetKind, Id, IsApproved, ByteLength, CreatedUtc, SourceLabel
FROM SceneImageReferenceAssets
WHERE IdentityPackId = '2d13c667-a690-4b5a-992a-93359591aa41'
ORDER BY FaceView, CreatedUtc;
