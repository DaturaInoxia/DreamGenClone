-- The seeded LoRA coverage-cell prompt rows, so the actual wording can be read from the live DB before any
-- UI exists. They are inserted on app startup: an app that predates the change will not have them, and the
-- fix is a restart, not a re-seeded row.
SELECT t.Key, t.WorkflowStep, length(t.Body) AS BodyChars, t.Body
FROM ImageWorkflowPromptTemplates t
WHERE t.Key LIKE 'lora.%'
ORDER BY t.Key;
