SELECT
    p.Id AS ProviderId,
    p.Name AS ProviderName,
    p.ProviderType,
    p.BaseUrl,
    p.IsEnabled,
    p.ImageProtocol,
    p.ImageCapability,
    p.ContentPolicy,
    rm.Id AS RegisteredModelId,
    rm.DisplayName AS ModelDisplayName,
    rm.ModelIdentifier,
    rm.ModelKind,
    rm.IsEnabled AS ModelIsEnabled,
    rm.IdentityMechanism,
    rm.IdentityStrength,
    rm.IdentityAdapterRef,
    rm.IdentityClipVisionRef
FROM Providers p
LEFT JOIN RegisteredModels rm ON rm.ProviderId = p.Id
WHERE p.ImageCapability <> 'None'
   OR p.ImageProtocol IN ('OpenAiImages', 'ComfyUi', 'ComfyUiServerless')
ORDER BY p.Name, rm.DisplayName;
