-- B-135 B135-001/002: the real checkpoint identifiers for the compiler-profile seed.
-- Never guess these: a profile is keyed by the checkpoint the model actually resolves to.
SELECT Id,
       DisplayName,
       ModelIdentifier,
       SceneImageModelFamily,
       PromptDialect
FROM RegisteredModels
WHERE SceneImageModelFamily > 0
ORDER BY SceneImageModelFamily, DisplayName;
