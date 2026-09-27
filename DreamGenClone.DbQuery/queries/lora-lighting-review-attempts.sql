-- Read-only: the prompts actually sent for the LoRA cell attempts, with the batch id that names the cell.
-- ModelSnapshotJson carries the model identity; NegativePrompt and PromptCompilerId show what was compiled.
SELECT substr(CandidateBatchId, 12) AS CellKey,
       CandidateBatchId AS Batch,
       Status,
       length(Prompt) AS Chars,
       COALESCE(NegativePrompt, '<none>') AS NegativePrompt,
       COALESCE(PromptCompilerId, '<none>') AS Compiler,
       json_extract(ModelSnapshotJson, '$.modelId') AS ModelId,
       json_extract(ModelSnapshotJson, '$.displayName') AS ModelName,
       FileRelativePath,
       CreatedUtc,
       Prompt
FROM SceneAssetImages
WHERE CandidateBatchId LIKE 'lora-cell-%'
ORDER BY CandidateBatchId, CreatedUtc;
