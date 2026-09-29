SELECT COUNT(*) AS RowsWhereBodyDiffersFromSeed FROM ImageWorkflowPromptTemplates WHERE (Key LIKE 'lora.cell.render.%' OR Key LIKE 'lora.vocabulary.lighting.%') AND Body <> SeedBody;
