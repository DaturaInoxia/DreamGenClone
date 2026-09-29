-- Guard check to run BEFORE image-preset-expression-fixups-cleanup.sql, and re-run AFTER it as the post-check.
--
-- The cleanup is guarded by `Body = SeedBody` so a hand-edited row is never overwritten. Expected BEFORE the run:
-- EditedRows = 0. Expected AFTER the run: EditedRows = 0 again, because the cleanup writes Body and SeedBody together
-- — equal means "this row carries the fix and nothing has edited it since".
SELECT COUNT(*) AS Rows,
       SUM(CASE WHEN Body <> SeedBody THEN 1 ELSE 0 END) AS EditedRows,
       group_concat(CASE WHEN Body <> SeedBody THEN Key END, ' | ') AS EditedKeys
FROM ImageWorkflowPromptTemplates
WHERE Key IN ('image.preset.expression.disgusted', 'image.preset.expression.goofy');
