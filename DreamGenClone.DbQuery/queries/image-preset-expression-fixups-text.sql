-- Reads the fixed rows back, so the wording is verified rather than assumed from the rows-affected count.
SELECT Key, Body, (Body = SeedBody) AS BodyMatchesSeed
FROM ImageWorkflowPromptTemplates
WHERE Key IN ('image.preset.expression.disgusted', 'image.preset.expression.goofy')
ORDER BY Key;
