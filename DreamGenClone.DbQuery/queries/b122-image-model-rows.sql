-- The registered IMAGE models the pickers list from: ModelKind=Image (1) and the two columns that decide whether a
-- model appears in the GENERATOR list at all (a model with an editor diffusion model set is excluded from
-- ListSceneImageModelsAsync).
SELECT m.Id, m.DisplayName, m.ModelIdentifier, m.ModelKind, m.IsEnabled,
       coalesce(m.ImageEditorDiffusionModel, '(none)') AS EditorDiffusionModel,
       m.SceneImageModelFamily, m.PromptDialect, coalesce(p.Name, '(no provider)') AS Provider
FROM RegisteredModels m
LEFT JOIN Providers p ON p.Id = m.ProviderId
ORDER BY m.ModelKind, m.DisplayName;
