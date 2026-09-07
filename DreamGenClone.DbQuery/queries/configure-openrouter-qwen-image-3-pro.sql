UPDATE RegisteredModels
SET SceneImageModelFamily = 3,
    PromptDialect = 3
WHERE Id = 'b162c572-db3f-4e94-8d6b-c7529ef9b37e'
  AND ProviderId = '86e7c25e-c736-4c99-b418-f3512689546a'
  AND lower(ModelIdentifier) = 'qwen/qwen-image-3-pro';