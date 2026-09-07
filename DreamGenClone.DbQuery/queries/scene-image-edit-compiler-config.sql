SELECT
    defaults.FunctionName,
    models.Id AS ModelId,
    models.DisplayName AS ModelDisplayName,
    models.ModelIdentifier,
    models.IsEnabled AS ModelEnabled,
    models.SupportsImageInput,
    providers.Id AS ProviderId,
    providers.Name AS ProviderName,
    providers.BaseUrl,
    providers.IsEnabled AS ProviderEnabled,
    providers.CredentialReference AS CredentialReferenceName,
    CASE WHEN TRIM(COALESCE(providers.CredentialReference, '')) = '' THEN 'missing' ELSE 'configured' END AS CredentialReference,
    CASE WHEN TRIM(COALESCE(providers.ApiKeyEncrypted, '')) = '' THEN 'missing' ELSE 'configured' END AS InferenceCredential
FROM FunctionModelDefaults AS defaults
JOIN RegisteredModels AS models ON models.Id = defaults.ModelId
JOIN Providers AS providers ON providers.Id = models.ProviderId
WHERE defaults.FunctionName = 'RolePlaySceneImageEditPromptCompiler';