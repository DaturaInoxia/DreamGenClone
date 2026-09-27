-- Read-only: the pack reference images the LoRA cells condition on (Becky's approved BodyComplete pack),
-- with their file paths so the lighting of the references themselves can be inspected.
SELECT a.Id, a.AssetKind, a.FaceView, a.BodyView, a.BodyState, a.IsApproved, a.FileRelativePath
FROM SceneImageReferenceAssets a
JOIN CharacterImageIdentityPacks p ON p.Id = a.IdentityPackId
WHERE p.CharacterProfileId = (SELECT CharacterProfileId FROM CharacterLoraDatasets LIMIT 1)
  AND p.Status = 'Approved'
  AND a.IsApproved = 1
ORDER BY a.AssetKind, a.FaceView, a.BodyView, a.BodyState;
