-- Every identity pack of this character with its asset count and the build that promoted into it: the pack the
-- operator's message named can be compared with the build whose views are CURRENTLY accepted. {{id}} is the
-- character template id.
SELECT p.Id, p.Version, p.Status, p.PackScope, p.CreatedUtc,
       (SELECT COUNT(*) FROM SceneImageReferenceAssets a WHERE a.IdentityPackId = p.Id) AS Assets,
       (SELECT COUNT(*) FROM SceneImageReferenceAssets a WHERE a.IdentityPackId = p.Id AND a.IsApproved = 1) AS Approved,
       (SELECT b.Id FROM CharacterIdentityBuilds b WHERE b.ProducedIdentityPackId = p.Id) AS PromotedByBuild
FROM CharacterImageIdentityPacks p
WHERE p.CharacterProfileId = '{{id}}'
ORDER BY p.Version DESC;
