SELECT COUNT(*) AS AngleRecords FROM CharacterIdentityAngles WHERE BuildId = '49c3175356874e908b181cad815f7141';
SELECT COUNT(*) AS AngleAttempts FROM CharacterIdentityAngleAttempts WHERE AngleId IN (SELECT Id FROM CharacterIdentityAngles WHERE BuildId = '49c3175356874e908b181cad815f7141');
SELECT Id, CurrentStep, Status, CanonicalFrontAssetId FROM CharacterIdentityBuilds WHERE Id = '49c3175356874e908b181cad815f7141';
SELECT Id, Status, SourceImageId, CandidateBatchId FROM SceneAssetImages WHERE Id = '8bcf8df809cb481286b88349d182f6f5';
