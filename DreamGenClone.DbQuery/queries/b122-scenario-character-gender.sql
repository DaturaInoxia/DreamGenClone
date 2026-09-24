-- The scenario character behind a /characters/{id} route: the characters live in the scenario's payload JSON, and the
-- studio reads the character's Gender from there. {{id}} is the scenario character id (the route id).
SELECT s.Id AS ScenarioId, s.Name AS ScenarioName, substr(s.PayloadJson, 1, 300) AS PayloadStart
FROM Scenarios s
WHERE s.PayloadJson LIKE '%' || '{{id}}' || '%';
