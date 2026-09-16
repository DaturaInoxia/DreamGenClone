SELECT Id, CharacterProfileId, Version, Status, PackScope, CanonicalFaceAssetId, CreatedUtc FROM CharacterImageIdentityPacks WHERE Id = '0ec16030-a876-4f63-ad83-fa8040b13ae8';
SELECT Id, IdentityPackId, AssetKind, FaceView, ViewDescriptorJson, FileRelativePath, CreatedUtc FROM SceneImageReferenceAssets WHERE IdentityPackId = '0ec16030-a876-4f63-ad83-fa8040b13ae8' ORDER BY FaceView;
SELECT Id, CharacterProfileId, ProducedIdentityPackId, CurrentStep, Status FROM CharacterIdentityBuilds WHERE ProducedIdentityPackId = '0ec16030-a876-4f63-ad83-fa8040b13ae8';
