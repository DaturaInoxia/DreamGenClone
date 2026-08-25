SELECT
    p.Id AS ProviderId,
    p.Name AS ProviderName,
    p.BaseUrl,
    p.ReadinessPath,
    p.LifecycleStrategyIdentifier,
    p.IsEnabled,
    m.Id AS ModelId,
    m.DisplayName AS ModelName,
    m.ModelIdentifier
FROM Providers p
LEFT JOIN RegisteredModels m ON m.ProviderId = p.Id
WHERE p.BaseUrl LIKE '%runpod%'
   OR p.Name LIKE '%RunPod%'
   OR p.LifecycleStrategyIdentifier = 'ManagedDedicatedPod'
ORDER BY p.Name, m.DisplayName;