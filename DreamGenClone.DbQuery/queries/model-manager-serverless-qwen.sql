SELECT
    p.Id AS ProviderId,
    p.Name AS ProviderName,
    p.BaseUrl,
    p.ImageProtocol,
    p.LifecycleStrategyIdentifier,
    p.CredentialReference,
    p.TimeoutSeconds,
    p.IsEnabled AS ProviderEnabled,
    rm.Id AS ModelId,
    rm.DisplayName,
    rm.ModelIdentifier,
    rm.ImageEditorDiffusionModel,
    rm.ImageEditorSteps,
    rm.ImageEditorCfg,
    rm.ImageEditorSampler,
    rm.ImageEditorScheduler,
    rm.ImageEditorDenoise,
    rm.IsEnabled AS ModelEnabled,
    f.FunctionName
FROM FunctionModelDefaults f
INNER JOIN RegisteredModels rm ON rm.Id = f.ModelId
INNER JOIN Providers p ON p.Id = rm.ProviderId
WHERE f.FunctionName IN (
    'RolePlaySceneImageEditor',
    'RolePlaySceneImageEditPromptCompiler'
)
ORDER BY p.Id, rm.Id, f.FunctionName;