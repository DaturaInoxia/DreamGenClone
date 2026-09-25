-- Does the new shared media-edit store already hold asset-image sessions? If it is still empty for
-- SubjectKind='AssetImage', the legacy SceneAssetImageEditSessions tables are the only live ones and the
-- delete cascade only has to handle those.
SELECT
    (SELECT COUNT(*) FROM MediaEditSessions WHERE SubjectKind = 'AssetImage') AS NewStoreAssetImageSessions,
    (SELECT COUNT(*) FROM MediaEditSessions) AS NewStoreAllSessions,
    (SELECT COUNT(*) FROM SceneAssetImageEditSessions) AS LegacyAssetImageSessions;
