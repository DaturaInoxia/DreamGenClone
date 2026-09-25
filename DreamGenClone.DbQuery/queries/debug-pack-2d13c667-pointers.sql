-- The pointers the promoted pack still carries: these are what identity conditioning and the pack screen read per
-- slot, which is why the NEW promoted assets are not the ones being shown/used.
SELECT Id, Version, Status, PackScope, CanonicalFaceAssetId, CanonicalFullBodyAssetId, DescriptorSnapshotJson,
       ApprovedUtc
FROM CharacterImageIdentityPacks
WHERE Id = '2d13c667-a690-4b5a-992a-93359591aa41';
