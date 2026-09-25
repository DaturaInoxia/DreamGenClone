-- The edit sessions of the exact images involved in the three-quarter-left work: the workspace goes ReadOnly when
-- the session it resolves for a subject is already Completed, which is what disables "Prepare edit".
SELECT Id, SourceImageId, Status, CreatedUtc, CompletedUtc, substr(COALESCE(DescriptionText, ''), 1, 50) AS Description
FROM SceneAssetImageEditSessions
WHERE SourceImageId IN (
    '3b8f7dd9d0d84c6cb53f371b2b8df39e', '77a0862a82f04c9dbb9bd2c5b36bb35b',
    'aa72679761a846e7b42768cd554de61c', '9ea464b9e64f40219fb9eaa08e7e6c86',
    '496155b9bce34e90bbf2a3d728930e10', '7f6beb71465846a2860171cd33ccdb37',
    '666adf6885074d599aec40493597ed48')
ORDER BY CreatedUtc;
