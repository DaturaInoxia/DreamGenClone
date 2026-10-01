-- The scenario's own setting material: its Locations, its Environmental Details, and its cast.
-- This is the source the SFW Baseline catalog's locations are drawn from (paired down) and enriched.
SELECT 'LOCATION' AS Kind, json_extract(l.value, '$.Name') AS Name, json_extract(l.value, '$.Description') AS Detail
FROM Scenarios s, json_each(s.PayloadJson, '$.Locations') l
WHERE s.Id = '135a9237-bcc1-45ee-afa1-ff2f14c49e50'
UNION ALL
SELECT 'ENV', 'detail ' || e.key, e.value
FROM Scenarios s, json_each(s.PayloadJson, '$.Setting.EnvironmentalDetails') e
WHERE s.Id = '135a9237-bcc1-45ee-afa1-ff2f14c49e50'
UNION ALL
SELECT 'CHARACTER', json_extract(c.value, '$.Name'), json_extract(c.value, '$.Description')
FROM Scenarios s, json_each(s.PayloadJson, '$.Characters') c
WHERE s.Id = '135a9237-bcc1-45ee-afa1-ff2f14c49e50';
