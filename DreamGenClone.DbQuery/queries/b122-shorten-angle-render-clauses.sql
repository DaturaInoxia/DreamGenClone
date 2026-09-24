-- Shorten the four angle-render camera clauses in the LIVE database.
--
-- Why: the angle render appends this clause to the compiled body prompt, which is validated against the family ceiling
-- of 800 characters (§2.2). The original seeded wording was ~330 characters, so a normal 563-character body prompt
-- became 891 and every angle render was refused with [over-length] — the operator saw "Render angle does nothing".
--
-- Body AND SeedBody are both written, exactly as a fresh seed would: leaving SeedBody at the long text would make the
-- row read as an operator override and a "reset to seed" would restore the broken wording.
UPDATE ImageWorkflowPromptTemplates
SET Body = CASE Key
        WHEN 'identity.body.angle.render.three-quarter'
            THEN 'Camera slightly to the front-left, body turned three-quarters away, left side nearer the camera, facing left of frame.'
        WHEN 'identity.body.angle.render.three-quarter.right'
            THEN 'Camera slightly to the front-right, body turned three-quarters away, right side nearer the camera, facing right of frame.'
        WHEN 'identity.body.angle.render.profile'
            THEN 'Camera directly to the left, body edge-on in full left profile, nose pointing to the left of frame.'
        WHEN 'identity.body.angle.render.profile.right'
            THEN 'Camera directly to the right, body edge-on in full right profile, nose pointing to the right of frame.'
    END,
    SeedBody = CASE Key
        WHEN 'identity.body.angle.render.three-quarter'
            THEN 'Camera slightly to the front-left, body turned three-quarters away, left side nearer the camera, facing left of frame.'
        WHEN 'identity.body.angle.render.three-quarter.right'
            THEN 'Camera slightly to the front-right, body turned three-quarters away, right side nearer the camera, facing right of frame.'
        WHEN 'identity.body.angle.render.profile'
            THEN 'Camera directly to the left, body edge-on in full left profile, nose pointing to the left of frame.'
        WHEN 'identity.body.angle.render.profile.right'
            THEN 'Camera directly to the right, body edge-on in full right profile, nose pointing to the right of frame.'
    END
WHERE Key IN (
    'identity.body.angle.render.three-quarter',
    'identity.body.angle.render.three-quarter.right',
    'identity.body.angle.render.profile',
    'identity.body.angle.render.profile.right');
