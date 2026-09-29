-- The presets' stored text after the flush tone-down, so the wording is READ rather than inferred from a count.
-- `BodyMatchesSeed` must be 1: the row is unedited relative to the new seed, so "Reset to default" keeps the tone-down
-- instead of restoring the sunburn.
SELECT Key, Body, (Body = SeedBody) AS BodyMatchesSeed
FROM ImageWorkflowPromptTemplates
WHERE Key IN (
    'image.preset.expression.sensual',
    'image.preset.expression.aroused',
    'image.preset.expression.orgasm',
    'image.preset.expression.ahegao',
    'image.preset.expression.orgasm-intense'
)
ORDER BY Key;
