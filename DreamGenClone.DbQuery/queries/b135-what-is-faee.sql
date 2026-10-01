-- What is faee1ec0-1cf3-459e-97d2-ad59717c41ba? It owns 25 CharacterFace + 11 CharacterBody assets named
-- "Dean", but it is NOT the id of the approved pack owner for Dean (a4894571...). Answered from every namespace
-- an identity owner id can live in, plus the assets that carry it.
SELECT 'SceneAsset' AS Namespace, s.Id AS Id, s.Name AS Label, s.Type AS Detail
FROM SceneAssets s
WHERE s.Id = 'faee1ec0-1cf3-459e-97d2-ad59717c41ba'
UNION ALL
SELECT 'Template', t.Id, t.Name, t.TemplateType
FROM Templates t
WHERE t.Id = 'faee1ec0-1cf3-459e-97d2-ad59717c41ba'
UNION ALL
SELECT 'CharacterProfile', p.Id, p.Name, p.TargetRole
FROM CharacterProfiles p
WHERE p.Id = 'faee1ec0-1cf3-459e-97d2-ad59717c41ba'
UNION ALL
SELECT 'IdentityPackOwner', p.CharacterProfileId, p.CharacterProfileId, 'pack v' || p.Version
FROM CharacterImageIdentityPacks p
WHERE p.CharacterProfileId = 'faee1ec0-1cf3-459e-97d2-ad59717c41ba';
