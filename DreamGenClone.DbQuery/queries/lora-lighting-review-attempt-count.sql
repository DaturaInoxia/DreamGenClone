-- Read-only: how many LoRA cell attempts exist at all (renders happen through the cell service).
SELECT COUNT(*) AS CellAttempts,
       SUM(CASE WHEN Status = 'Succeeded' THEN 1 ELSE 0 END) AS Succeeded
FROM SceneAssetImages
WHERE CandidateBatchId LIKE 'lora-cell-%';
