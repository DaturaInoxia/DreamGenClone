SELECT
    rm.Id,
    rm.DisplayName,
    rm.ModelIdentifier,
    rm.IdentityMechanism,
    rm.IdentityStrength,
    rm.IdentityAdapterRef,
    rm.IdentityClipVisionRef,
    rm.SupportedIdentityStrategiesJson,
    rm.SupportedVisualStrategiesJson,
    rm.CapabilityQualificationsJson,
    rm.Notes,
    p.Name AS ProviderName
FROM RegisteredModels rm
JOIN Providers p ON p.Id = rm.ProviderId
WHERE rm.Id = '9142a254-2779-4002-9e18-2d796e1e9fbd';