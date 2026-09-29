-- Forward update: the tone-down of the flush wording, for a dev DB whose rows were seeded BEFORE the change.
--
-- Why this is needed at all: the seed is `INSERT OR IGNORE`, so re-worded seed bodies never overwrite existing rows
-- (by design — an operator's edit wins). The fix for a stale seeded row is therefore an explicit forward UPDATE, not
-- a re-seed.
--
-- Reported 2026-09-27: renders "coming out looking sun burnt" on the flush presets. The wording was the cause:
-- "flush" already means a red wash, "heavy"/"deep"/"warm" raise its chroma without raising the emotional read,
-- "across the cheeks" reads as a band across the face, and naming the NOSE as red is the sunburn line itself.
-- Now: the nose is never coloured (heat on it is sheen), colour is pinned to the apples of the cheeks, the rest of
-- the face is explicitly told to keep its own skin tone — the guard that stops the spread — and sweat carries the
-- heat read, which cannot sunburn because it has no colour.
--
-- The guard `Body = SeedBody` is the "an operator's edit wins" rule applied to a bulk update: it touches ONLY rows
-- nobody has edited. Verified with image-preset-flush-guard-check.sql before running.
-- The two CASE copies exist because a row's SeedBody must move with its Body — "Reset to default" restores SeedBody,
-- so leaving it on the old wording would silently undo this cleanup on the next reset.
--
-- Run with: helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/image-preset-flush-cleanup.sql
UPDATE ImageWorkflowPromptTemplates
SET Body = CASE Key
        WHEN 'image.preset.expression.sensual' THEN 'the eyes half-lidded with a heavy downward gaze, the brows relaxed and lifted at the inner ends, the lips softly parted and slightly full, the mouth corners relaxed and a soft rose on the apples of the cheeks, the rest of the face keeping its own even skin tone'
        WHEN 'image.preset.expression.aroused' THEN 'the eyes half-closed and heavy with the pupils dilated, the gaze dropped and steady, the brows relaxed and lifted at the inner ends, the lips parted and slightly swollen with the lower lip drawn in between the teeth, the jaw loose, the mouth corners slack and a soft rose on the apples of the cheeks, the rest of the face keeping its own even skin tone'
        WHEN 'image.preset.expression.orgasm' THEN 'the eyes rolled up and fluttering behind barely open lids, the brows drawn together and lifted at the inner ends, the jaw dropped slack with the mouth held open wide, the nostrils flared, a fine sheen of sweat on the cheeks and throat and the head tipped back'
        WHEN 'image.preset.expression.ahegao' THEN 'the eyes crossed inward so the pupils converge toward the bridge of the nose with the irises still visible in both, the eyelids held wide, the mouth open wide and completely slack, the tongue pushed out and lolling over the lower lip, the brows raised, a fine sheen of sweat over the cheeks and the tip of the nose and the head tipped back a little'
        WHEN 'image.preset.expression.orgasm-intense' THEN 'the eyes rolled back so only the whites show beneath lids that flutter half-closed, the gaze unfocused and turned up and inward, the brows drawn together and lifted, the jaw dropped fully open with the lips slack and glistening, the head tipped back with the neck extended, a fine sheen of sweat high on the cheeks and the whole face gone slack'
    END,
    SeedBody = CASE Key
        WHEN 'image.preset.expression.sensual' THEN 'the eyes half-lidded with a heavy downward gaze, the brows relaxed and lifted at the inner ends, the lips softly parted and slightly full, the mouth corners relaxed and a soft rose on the apples of the cheeks, the rest of the face keeping its own even skin tone'
        WHEN 'image.preset.expression.aroused' THEN 'the eyes half-closed and heavy with the pupils dilated, the gaze dropped and steady, the brows relaxed and lifted at the inner ends, the lips parted and slightly swollen with the lower lip drawn in between the teeth, the jaw loose, the mouth corners slack and a soft rose on the apples of the cheeks, the rest of the face keeping its own even skin tone'
        WHEN 'image.preset.expression.orgasm' THEN 'the eyes rolled up and fluttering behind barely open lids, the brows drawn together and lifted at the inner ends, the jaw dropped slack with the mouth held open wide, the nostrils flared, a fine sheen of sweat on the cheeks and throat and the head tipped back'
        WHEN 'image.preset.expression.ahegao' THEN 'the eyes crossed inward so the pupils converge toward the bridge of the nose with the irises still visible in both, the eyelids held wide, the mouth open wide and completely slack, the tongue pushed out and lolling over the lower lip, the brows raised, a fine sheen of sweat over the cheeks and the tip of the nose and the head tipped back a little'
        WHEN 'image.preset.expression.orgasm-intense' THEN 'the eyes rolled back so only the whites show beneath lids that flutter half-closed, the gaze unfocused and turned up and inward, the brows drawn together and lifted, the jaw dropped fully open with the lips slack and glistening, the head tipped back with the neck extended, a fine sheen of sweat high on the cheeks and the whole face gone slack'
    END
WHERE Key IN (
        'image.preset.expression.sensual',
        'image.preset.expression.aroused',
        'image.preset.expression.orgasm',
        'image.preset.expression.ahegao',
        'image.preset.expression.orgasm-intense'
    )
    AND Body = SeedBody;
