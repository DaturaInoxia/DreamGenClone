-- Which scenario names the id that owns the Dean face/body assets? A scenario character is a GUID inside the
-- scenario's own JSON, which is why that id is in no id-keyed table of its own.
SELECT s.Id AS ScenarioId, s.Name AS ScenarioName
FROM Scenarios s
WHERE s.PayloadJson LIKE '%faee1ec0-1cf3-459e-97d2-ad59717c41ba%';
