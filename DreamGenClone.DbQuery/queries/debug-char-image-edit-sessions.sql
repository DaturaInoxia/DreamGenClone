-- The media-edit sessions recorded against this character's front-container images: these rows carry
-- FOREIGN KEY (SourceImageId) REFERENCES SceneAssetImages(Id) ON DELETE RESTRICT, which is what makes
-- "delete the uploaded image" fail with SQLite Error 19. {{id}} is the character template id.
SELECT s.Id AS SessionId, s.SourceImageId, s.SourceImageSha256, s.Status, s.CompletedUtc,
       substr(COALESCE(s.DescriptionText, ''), 1, 60) AS Description,
       (SELECT i.FileRelativePath FROM SceneAssetImages i WHERE i.Id = s.SourceImageId) AS SourceFile
FROM SceneAssetImageEditSessions s
WHERE s.SourceImageId IN (
        SELECT i.Id FROM SceneAssetImages i
        WHERE i.AssetId IN (
            SELECT b.FrontContainerAssetId FROM CharacterIdentityBuilds b WHERE b.CharacterProfileId = '{{id}}'
        )
    )
ORDER BY s.CreatedUtc DESC
LIMIT 40;
