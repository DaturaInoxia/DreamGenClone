SELECT
    p.Id AS ProviderId,
    p.Name AS ProviderName,
    p.BaseUrl,
    p.ImageCapability,
    p.ImageProtocol,
    p.ContentPolicy,
    p.IsEnabled AS ProviderEnabled,
    rm.Id AS ModelId,
    rm.DisplayName,
    rm.ModelIdentifier,
    rm.ModelKind,
    rm.ImageEditorDiffusionModel,
    rm.ImageEditorTextEncoder,
    rm.ImageEditorVae,
    rm.ImageEditorSteps,
    rm.ImageEditorCfg,
    rm.ImageEditorSampler,
    rm.ImageEditorScheduler,
    rm.ImageEditorDenoise,
    rm.ImageEditorAuraFlowShift,
    rm.ImageEditorCfgNormStrength,
    rm.IsEnabled AS ModelEnabled
FROM Providers p
LEFT JOIN RegisteredModels rm ON rm.ProviderId = p.Id
WHERE p.ImageCapability <> 0
ORDER BY p.Name, rm.DisplayName;

SELECT
    f.Id,
    f.FunctionName,
    f.ModelId,
    rm.DisplayName,
    rm.ModelIdentifier
FROM FunctionModelDefaults f
LEFT JOIN RegisteredModels rm ON rm.Id = f.ModelId
WHERE f.FunctionName IN ('RolePlaySceneImage', 'RolePlaySceneImageEditor')
ORDER BY f.FunctionName;