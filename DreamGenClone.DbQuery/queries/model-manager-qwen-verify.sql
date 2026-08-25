SELECT
    p.Name AS ProviderName,
    p.BaseUrl,
    p.ImageCapability,
    p.ImageProtocol,
    p.ContentPolicy,
    p.TimeoutSeconds,
    rm.DisplayName AS ModelName,
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
    f.FunctionName
FROM FunctionModelDefaults f
INNER JOIN RegisteredModels rm ON rm.Id = f.ModelId
INNER JOIN Providers p ON p.Id = rm.ProviderId
WHERE f.FunctionName = 'RolePlaySceneImageEditor';