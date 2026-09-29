-- Forward update: the cleaned LoRA cell prompt wording, for a dev DB whose rows were seeded BEFORE the cleanup.
--
-- Why this is needed at all: the seed is `INSERT OR IGNORE`, so re-worded seed bodies never overwrite existing rows
-- (by design — an operator's edit wins). The fix for a stale seeded row is therefore an explicit forward UPDATE, not
-- a re-seed.
--
-- The guard `Body = SeedBody` is the "an operator's edit wins" rule applied to a bulk update: it touches ONLY rows
-- nobody has edited. Verified on the live dev DB before running: 0 of these rows had Body <> SeedBody.
-- The two CASE copies exist because a row's SeedBody must move with its Body — "Reset to seed" restores SeedBody, so
-- leaving it on the old wording would silently undo this cleanup.
--
-- Run with: helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/lora-cell-prompt-cleanup.sql
UPDATE ImageWorkflowPromptTemplates
SET Body = CASE Key
        WHEN 'lora.cell.render.front.close' THEN 'Photorealistic close-up photograph of {Subject}. {Facing}, the whole head and both shoulders in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.front.half' THEN 'Photorealistic photograph of {Subject} from the waist up. {Facing}, the whole upper body and both hands in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.front.full' THEN 'Photorealistic full-body photograph of {Subject}, head to feet. {Facing}, the whole body in frame and unobstructed. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.threequarter.close' THEN 'Photorealistic close-up photograph of {Subject}. The head and shoulders are turned three-quarters away from the camera, {Facing}, one cheek nearer the camera than the other, the whole head and both shoulders in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.threequarter.half' THEN 'Photorealistic photograph of {Subject} from the waist up, the head and upper body turned three-quarters away from the camera, {Facing}, one side of the body nearer the camera, the whole upper body and both hands in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.threequarter.full' THEN 'Photorealistic full-body photograph of {Subject}, head to feet, the body turned three-quarters away from the camera, {Facing}, one side of the body nearer the camera, the whole body in frame and unobstructed. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.profile.close' THEN 'Photorealistic close-up photograph of {Subject} in full profile, seen from the side, {Facing}, a true edge-on profile of the head and shoulders rather than a turned head. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.profile.half' THEN 'Photorealistic photograph of {Subject} from the waist up in full profile, seen from the side, {Facing}, a true edge-on profile of the head and upper body. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.profile.full' THEN 'Photorealistic full-body photograph of {Subject}, head to feet, in full profile seen from the side, {Facing}, a true edge-on profile of the whole body. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.behind.close' THEN 'Photorealistic close-up photograph of {Subject} from behind, {Facing}, the back of the head and both shoulders in frame, the face not visible. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.behind.half' THEN 'Photorealistic photograph of {Subject} from behind, from the waist up, {Facing}, the back of the head, the shoulders and the back in frame, the face not visible. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.behind.full' THEN 'Photorealistic full-body photograph of {Subject}, head to feet, from behind, {Facing}, the back of the head, the back, the backside and the backs of the legs in frame, the face not visible, the whole body in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.vocabulary.lighting.indoor-bright' THEN 'bright, even lighting with soft shadows'
        WHEN 'lora.vocabulary.lighting.indoor-dim' THEN 'dim, low-key lighting with soft shadows'
        WHEN 'lora.vocabulary.lighting.outdoor-day' THEN 'flat overcast daylight, soft and even'
        WHEN 'lora.vocabulary.lighting.outdoor-golden' THEN 'warm golden-hour sunlight from the side, long shadows'
        WHEN 'lora.vocabulary.lighting.outdoor-night' THEN 'low ambient light at night, one practical light source, deep shadows'
        WHEN 'lora.vocabulary.lighting.hard-rim' THEN 'a hard rim light along the edge of the body, the far side in deep shadow'
    END,
    SeedBody = CASE Key
        WHEN 'lora.cell.render.front.close' THEN 'Photorealistic close-up photograph of {Subject}. {Facing}, the whole head and both shoulders in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.front.half' THEN 'Photorealistic photograph of {Subject} from the waist up. {Facing}, the whole upper body and both hands in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.front.full' THEN 'Photorealistic full-body photograph of {Subject}, head to feet. {Facing}, the whole body in frame and unobstructed. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.threequarter.close' THEN 'Photorealistic close-up photograph of {Subject}. The head and shoulders are turned three-quarters away from the camera, {Facing}, one cheek nearer the camera than the other, the whole head and both shoulders in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.threequarter.half' THEN 'Photorealistic photograph of {Subject} from the waist up, the head and upper body turned three-quarters away from the camera, {Facing}, one side of the body nearer the camera, the whole upper body and both hands in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.threequarter.full' THEN 'Photorealistic full-body photograph of {Subject}, head to feet, the body turned three-quarters away from the camera, {Facing}, one side of the body nearer the camera, the whole body in frame and unobstructed. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.profile.close' THEN 'Photorealistic close-up photograph of {Subject} in full profile, seen from the side, {Facing}, a true edge-on profile of the head and shoulders rather than a turned head. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.profile.half' THEN 'Photorealistic photograph of {Subject} from the waist up in full profile, seen from the side, {Facing}, a true edge-on profile of the head and upper body. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.profile.full' THEN 'Photorealistic full-body photograph of {Subject}, head to feet, in full profile seen from the side, {Facing}, a true edge-on profile of the whole body. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.behind.close' THEN 'Photorealistic close-up photograph of {Subject} from behind, {Facing}, the back of the head and both shoulders in frame, the face not visible. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.behind.half' THEN 'Photorealistic photograph of {Subject} from behind, from the waist up, {Facing}, the back of the head, the shoulders and the back in frame, the face not visible. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.cell.render.behind.full' THEN 'Photorealistic full-body photograph of {Subject}, head to feet, from behind, {Facing}, the back of the head, the back, the backside and the backs of the legs in frame, the face not visible, the whole body in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail.'
        WHEN 'lora.vocabulary.lighting.indoor-bright' THEN 'bright, even lighting with soft shadows'
        WHEN 'lora.vocabulary.lighting.indoor-dim' THEN 'dim, low-key lighting with soft shadows'
        WHEN 'lora.vocabulary.lighting.outdoor-day' THEN 'flat overcast daylight, soft and even'
        WHEN 'lora.vocabulary.lighting.outdoor-golden' THEN 'warm golden-hour sunlight from the side, long shadows'
        WHEN 'lora.vocabulary.lighting.outdoor-night' THEN 'low ambient light at night, one practical light source, deep shadows'
        WHEN 'lora.vocabulary.lighting.hard-rim' THEN 'a hard rim light along the edge of the body, the far side in deep shadow'
    END
WHERE Key IN (
        'lora.cell.render.front.close', 'lora.cell.render.front.half', 'lora.cell.render.front.full',
        'lora.cell.render.threequarter.close', 'lora.cell.render.threequarter.half', 'lora.cell.render.threequarter.full',
        'lora.cell.render.profile.close', 'lora.cell.render.profile.half', 'lora.cell.render.profile.full',
        'lora.cell.render.behind.close', 'lora.cell.render.behind.half', 'lora.cell.render.behind.full',
        'lora.vocabulary.lighting.indoor-bright', 'lora.vocabulary.lighting.indoor-dim',
        'lora.vocabulary.lighting.outdoor-day', 'lora.vocabulary.lighting.outdoor-golden',
        'lora.vocabulary.lighting.outdoor-night', 'lora.vocabulary.lighting.hard-rim'
    )
    AND Body = SeedBody;
