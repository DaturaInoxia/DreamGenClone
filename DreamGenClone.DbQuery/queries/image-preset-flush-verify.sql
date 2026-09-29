-- Verify AFTER image-preset-flush-cleanup.sql.
--
-- The word list is EXPLICIT on purpose. The first version of this check used `Body LIKE '%red%'`, which matches
-- "lowERED" and "redDENED" - it reported a false positive on `image.preset.expression.disgusted` ("the nose wrinkled
-- with creases across the bridge, the eyebrows lowered, ..."), a prompt with no colour in it at all. A check that cries
-- wolf is worse than no check, so the terms that actually produce a colour wash are named one by one.
--
-- Expected after the run: 0.
SELECT COUNT(*) AS RowsWithColourWashWording,
       group_concat(Key, ' | ') AS Keys
FROM ImageWorkflowPromptTemplates
WHERE Body LIKE '%flush%'
   OR Body LIKE '%sunburn%'
   OR Body LIKE '%redness%'
   OR Body LIKE '%reddish%'
   OR Body LIKE '%blotch%'
   OR Body LIKE '%red nose%'
   OR Body LIKE '%red cheeks%'
   OR Body LIKE '%reddened cheek%'
   OR Body LIKE '%deep red%';
