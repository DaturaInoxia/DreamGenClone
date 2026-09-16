DELETE FROM CharacterIdentityAngleAttempts WHERE AngleId IN (SELECT Id FROM CharacterIdentityAngles WHERE BuildId = '49c3175356874e908b181cad815f7141');
