-- Forward update: the disgusted and goofy expression wording, for a dev DB whose rows were seeded BEFORE the change.
--
-- Why this is needed at all: the seed is `INSERT OR IGNORE`, so re-worded seed bodies never overwrite existing rows
-- (by design — an operator's edit wins). The fix for a stale seeded row is therefore an explicit forward UPDATE.
--
-- Reported 2026-09-27. Two separate reports, both about wording the model had to guess at:
--
--   disgusted — "contorted the face a bit, it was close just the bottom lip and cheek look wrong." Two causes:
--     * "the cheeks pushed up" raised BOTH cheeks, which is a snarl read, and is what distorted the cheek. The crinkle
--       belongs to the raised side only, with the other explicitly left smooth.
--     * the lower lip was never mentioned, so the model followed the raised upper lip with it. It is now named as
--       deliberately NOT following.
--     The sneer is pinned to ONE stated side ("on that same side") because the old text said "one side" and "one mouth
--     corner" separately, leaving the model to choose — and choosing differently for each contorts the mouth.
--
--   goofy — "the eyes crossed or squinting unevenly" offers the model two states joined by OR, and it has to pick.
--     Squinting unevenly is the read wanted, so the alternative is removed rather than left to chance.
--
-- The guard `Body = SeedBody` is the "an operator's edit wins" rule applied to a bulk update: it touches ONLY rows
-- nobody has edited. Verified with image-preset-expression-fixups-guard-check.sql before running.
-- Both CASE copies exist because a row's SeedBody must move with its Body — "Reset to default" restores SeedBody, so
-- leaving it on the old wording would silently undo this fix on the next reset.
--
-- Run with: helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/image-preset-expression-fixups-cleanup.sql
UPDATE ImageWorkflowPromptTemplates
SET Body = CASE Key
        WHEN 'image.preset.expression.disgusted' THEN 'the nose wrinkled with creases across the bridge, the eyebrows lowered and drawn together, the upper lip raised on one side only enough to expose the teeth on that side, the mouth corner on that same side pulled up with it, the lower lip kept relaxed and slightly pushed down rather than following the sneer, the cheek on the raised side crinkled while the other stays smooth, the eyes narrowed and the head turned slightly away'
        WHEN 'image.preset.expression.goofy' THEN 'one eyebrow cocked high while the other drops, the eyes squinting unevenly, the lips pulled to one side in a lopsided grin with the teeth showing, the jaw pushed forward and the tongue tucked into the cheek'
    END,
    SeedBody = CASE Key
        WHEN 'image.preset.expression.disgusted' THEN 'the nose wrinkled with creases across the bridge, the eyebrows lowered and drawn together, the upper lip raised on one side only enough to expose the teeth on that side, the mouth corner on that same side pulled up with it, the lower lip kept relaxed and slightly pushed down rather than following the sneer, the cheek on the raised side crinkled while the other stays smooth, the eyes narrowed and the head turned slightly away'
        WHEN 'image.preset.expression.goofy' THEN 'one eyebrow cocked high while the other drops, the eyes squinting unevenly, the lips pulled to one side in a lopsided grin with the teeth showing, the jaw pushed forward and the tongue tucked into the cheek'
    END
WHERE Key IN ('image.preset.expression.disgusted', 'image.preset.expression.goofy')
    AND Body = SeedBody;
