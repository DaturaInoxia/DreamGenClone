-- The JSON window around a character entry inside a scenario payload, so the stored Gender for Dean is readable.
-- {{id}} is the scenario character id (the /characters/{id} route id).
SELECT substr(s.PayloadJson, instr(s.PayloadJson, '{{id}}') - 200, 700) AS CharacterWindow
FROM Scenarios s
WHERE s.PayloadJson LIKE '%' || '{{id}}' || '%'
LIMIT 1;
