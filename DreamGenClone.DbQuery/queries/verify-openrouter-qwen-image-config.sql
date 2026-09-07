SELECT
    p.Name AS ProviderName,
    p.ImageCapability,
    p.ImageGenerationPath,
    p.ImageProtocol,
    p.ContentPolicy,
    rm.ModelIdentifier,
    rm.SceneImageModelFamily,
    rm.PromptDialect
FROM Providers p
JOIN RegisteredModels rm ON rm.ProviderId = p.Id
WHERE p.Name = 'OpenRouter'
  AND rm.ModelIdentifier = 'qwen/qwen-image-3-pro';