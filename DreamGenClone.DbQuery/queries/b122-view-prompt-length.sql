-- The compiled body prompt this build's front view resolved, with its length: the SDXL validator caps a body prompt at
-- 800 characters, so an angle clause appended to it only fits if this leaves room. {{id}} is the buildId.
SELECT v.State, v.BodyView, length(v.ResolvedPromptText) AS PromptChars, v.ResolvedPromptText
FROM CharacterIdentityBodyViews v
WHERE v.BuildId = '{{id}}';
