-- Read-only: every seeded LoRA row, so the actual wording in the live DB can be reviewed for contradictions.
SELECT Key, WorkflowStep, length(Body) AS Chars, Body
FROM ImageWorkflowPromptTemplates
WHERE Key LIKE 'lora.%'
ORDER BY Key;
