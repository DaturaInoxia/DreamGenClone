-- The Qwen image editor model's resolved provider + content policy (what a completed edit would record).
SELECT rm.Id AS ModelId, rm.ModelIdentifier, rm.DisplayName, rm.IsEnabled, rm.SceneImageModelFamily,
       p.Id AS ProviderId, p.Name AS ProviderName, p.ContentPolicy AS ImageContentPolicy, p.ImageProtocol
FROM RegisteredModels rm
JOIN Providers p ON p.Id = rm.ProviderId
WHERE rm.ModelIdentifier = 'qwen_image_edit_2511_fp8mixed.safetensors';
