SELECT
    p.Id AS ProviderId,
    p.Name AS ProviderName,
    p.BaseUrl,
    p.ReadinessPath,
    p.ReadinessSuccessContractJson,
    p.ServerIdentityPolicyJson,
    p.LifecycleStrategyIdentifier,
    m.Id AS ModelId,
    m.DisplayName AS ModelName,
    m.ModelIdentifier,
    m.IsEnabled
FROM Providers p
LEFT JOIN RegisteredModels m ON m.ProviderId = p.Id
WHERE p.Id = '2dde3563-589d-436a-bc60-d646a2da3c25'
ORDER BY m.DisplayName;
