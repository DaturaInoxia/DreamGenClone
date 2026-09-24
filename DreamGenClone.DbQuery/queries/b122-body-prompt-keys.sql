-- The seeded prompt rows for the body pipeline: proves the angle-RENDER keys are present in the live DB (they are
-- inserted on app startup, so a running app that predates the change will not have them).
SELECT t.Key, t.WorkflowStep, substr(t.Body, 1, 70) AS BodyStart, t.UpdatedUtc
FROM ImageWorkflowPromptTemplates t
WHERE t.Key LIKE 'identity.body%'
ORDER BY t.Key;
