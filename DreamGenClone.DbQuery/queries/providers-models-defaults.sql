-- Full provider/model/function-default picture for image-related functions
SELECT
    'PROVIDER' AS Kind,
    p.Id AS ProviderId,
    p.Name AS Name,
    p.BaseUrl,
    p.IsEnabled,
    p.ImageCapability,
    p.ImageGenerationPath,
    p.ContentPolicy,
    p.ImageProtocol,
    p.ProviderType,
    '' AS ModelId,
    '' AS ModelName,
    '' AS ModelIdentifier,
    '' AS FunctionName,
    '' AS DisplayName
FROM Providers p
UNION ALL
SELECT
    'MODEL' AS Kind,
    m.ProviderId AS ProviderId,
    p.Name AS Name,
    p.BaseUrl,
    m.IsEnabled AS IsEnabled,
    p.ImageCapability,
    p.ImageGenerationPath,
    p.ContentPolicy,
    p.ImageProtocol,
    p.ProviderType,
    m.Id AS ModelId,
    m.DisplayName AS ModelName,
    m.ModelIdentifier,
    '' AS FunctionName,
    '' AS DisplayName
FROM RegisteredModels m
JOIN Providers p ON p.Id = m.ProviderId
UNION ALL
SELECT
    'DEFAULT' AS Kind,
    f.ModelId AS ProviderId,
    p.Name AS Name,
    p.BaseUrl,
    m.IsEnabled AS IsEnabled,
    p.ImageCapability,
    p.ImageGenerationPath,
    p.ContentPolicy,
    p.ImageProtocol,
    p.ProviderType,
    m.Id AS ModelId,
    m.DisplayName AS ModelName,
    m.ModelIdentifier,
    f.FunctionName,
    '' AS DisplayName
FROM FunctionModelDefaults f
JOIN RegisteredModels m ON m.Id = f.ModelId
JOIN Providers p ON p.Id = m.ProviderId
WHERE f.FunctionName LIKE '%Image%' OR f.FunctionName LIKE '%Scene%' OR f.FunctionName LIKE '%Pose%' OR f.FunctionName LIKE '%Vision%'
ORDER BY Kind, Name;
