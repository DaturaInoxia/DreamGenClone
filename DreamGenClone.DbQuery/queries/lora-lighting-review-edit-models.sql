-- Read-only: which image-EDITING models are registered and enabled, so the relight option can name real models.
SELECT m.DisplayName, m.ModelIdentifier, m.IsEnabled,
       m.ImageEditorGraphKind, m.ImageEditorLoraName, m.ImageEditorSteps, m.ImageEditorCfg,
       p.Name AS Provider, p.BaseUrl
FROM RegisteredModels m
LEFT JOIN Providers p ON p.Id = m.ProviderId
WHERE m.ModelKind = 1
  AND (m.ImageEditorGraphKind IS NOT NULL OR m.ImageEditorDiffusionModel IS NOT NULL)
ORDER BY m.IsEnabled DESC, m.DisplayName;
