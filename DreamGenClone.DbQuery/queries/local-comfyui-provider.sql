SELECT
    p.Id AS ProviderId,
    p.Name AS ProviderName,
    p.BaseUrl,
    p.ImageProtocol,
    p.ImageCapability,
    p.ContentPolicy,
    p.IsEnabled AS ProviderEnabled,
    m.Id AS ModelId,
    m.DisplayName AS ModelName,
    m.ModelIdentifier,
    m.SceneImageModelFamily,
    m.PromptDialect,
    m.IsEnabled AS ModelEnabled
FROM Providers p
LEFT JOIN RegisteredModels m ON m.ProviderId = p.Id
WHERE p.Name = 'Local ComfyUI (WOOD-GAME-MAIN 5080)'
ORDER BY m.DisplayName;
