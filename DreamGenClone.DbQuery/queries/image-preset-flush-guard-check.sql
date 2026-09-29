-- Guard check to run BEFORE image-preset-flush-cleanup.sql.
--
-- The cleanup is guarded by `Body = SeedBody` so a hand-edited row is never overwritten. That guard is correct, but a
-- SKIPPED row is a row still rendering sunburnt, so this tells the operator whether the update covered everything
-- rather than leaving them to assume it did.
--
-- Expected before the run: EditedRows = 0. Any key reported in EditedKeys was hand-edited and is deliberately left
-- alone; change it in the app instead.
SELECT COUNT(*) AS Rows,
       SUM(CASE WHEN Body <> SeedBody THEN 1 ELSE 0 END) AS EditedRows,
       group_concat(CASE WHEN Body <> SeedBody THEN Key END, ' | ') AS EditedKeys
FROM ImageWorkflowPromptTemplates
WHERE Key IN (
    'image.preset.expression.sensual',
    'image.preset.expression.aroused',
    'image.preset.expression.orgasm',
    'image.preset.expression.ahegao',
    'image.preset.expression.orgasm-intense'
);
