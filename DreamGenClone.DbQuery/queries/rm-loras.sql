SELECT ProviderId, ModelIdentifier, DisplayName, ModelKind, SceneImageModelFamily
FROM RegisteredModels
WHERE lower(ModelIdentifier) LIKE '%lora%' OR lower(DisplayName) LIKE '%lora%'
   OR lower(ModelIdentifier) LIKE '%pose%' OR lower(DisplayName) LIKE '%pose%'
ORDER BY DisplayName;
