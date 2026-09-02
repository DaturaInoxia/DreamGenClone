SELECT
    p.Id AS ProviderId,
    p.Name AS ProviderName,
    p.BaseUrl,
    p.IsEnabled,
    p.ImageCapability,
    p.ImageGenerationPath,
    p.ContentPolicy,
    p.ImageProtocol,
    p.TimeoutSeconds,
    m.Id AS ModelId,
    m.DisplayName AS ModelName,
    m.ModelIdentifier
FROM Providers p
LEFT JOIN RegisteredModels m ON m.ProviderId = p.Id
WHERE p.BaseUrl LIKE '%runpod%'
   OR p.Name LIKE '%RunPod%'
   OR p.BaseUrl LIKE '%proxy.runpod%'
ORDER BY p.Name, m.DisplayName;
