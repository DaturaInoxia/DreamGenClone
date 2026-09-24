-- One character template's identity-relevant fields: the gender the body-axis pickers read, and the appearance the
-- prefill reads. {{id}} is the characterProfileId (the route id of /characters/{id}).
SELECT c.Id, c.Name, c.TargetGender, c.UpdatedUtc
FROM CharacterProfiles c
WHERE c.Id = '{{id}}';
