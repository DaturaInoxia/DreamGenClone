-- Read-only: the live curation policy, so advice about cell counts and diversity minima is concrete.
SELECT COALESCE(CharacterProfileId, 'global') AS Scope,
       json_extract(PayloadJson, '$.expectedCoreCellCount') AS CoreCells,
       json_extract(PayloadJson, '$.expectedVariationCellCount') AS VariationCells,
       json_extract(PayloadJson, '$.seedRangeStart') AS SeedStart,
       json_extract(PayloadJson, '$.seedRangeLength') AS SeedRange,
       json_extract(PayloadJson, '$.minimumDistinctLighting') AS MinLighting,
       json_extract(PayloadJson, '$.minimumDistinctBackgrounds') AS MinBackgrounds,
       json_extract(PayloadJson, '$.minimumPoseClasses') AS MinPoses,
       json_extract(PayloadJson, '$.minimumDistinctOutfits') AS MinOutfits,
       json_extract(PayloadJson, '$.minimumTrainMembers') AS MinTrain,
       json_extract(PayloadJson, '$.minimumValidationMembers') AS MinValidation
FROM CharacterLoraCurationPolicies;
