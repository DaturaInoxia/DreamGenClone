SELECT
    rm.Id,
    rm.DisplayName,
    rm.ModelIdentifier,
    rm.IsEnabled,
    p.Name AS ProviderName,
    p.ImageProtocol,
    p.ReadinessPath,
    p.ReadinessSuccessContractJson,
    p.LifecycleStrategyIdentifier,
    p.MaximumActiveRequests,
    p.QueueCapacity,
    rm.IdentityMechanism,
    rm.IdentityStrength,
    rm.IdentityAdapterRef,
    rm.SupportedVisualStrategiesJson,
    rm.CapabilityQualificationsJson
FROM RegisteredModels rm
JOIN Providers p ON p.Id = rm.ProviderId
WHERE rm.ModelIdentifier = 'bigLust_v16.safetensors'
   OR rm.DisplayName LIKE '%Qwen Image%'
ORDER BY rm.DisplayName;