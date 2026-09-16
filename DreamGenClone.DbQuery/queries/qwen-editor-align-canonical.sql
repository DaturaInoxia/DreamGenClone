-- Align Qwen Image Edit 2511 models with the canonical validated recipe:
-- 40 steps / CFG 4 / euler / simple (denoise 1, shift 3.1, CFGNorm 1 already correct)

-- Rapid-AIO v23 + Gay/Trans LoRA (winning config per evidence)
UPDATE RegisteredModels 
SET ImageEditorSteps = 40,
    ImageEditorCfg = 4.0,
    ImageEditorSampler = 'euler',
    ImageEditorScheduler = 'simple'
WHERE Id = 'd5f62be4-ec16-40c6-a14a-3da239df8acd';

-- Remix AIO v2.0 + Gay/Trans LoRA (alternative variant)
UPDATE RegisteredModels 
SET ImageEditorSteps = 40,
    ImageEditorCfg = 4.0,
    ImageEditorSampler = 'euler',
    ImageEditorScheduler = 'simple'
WHERE Id = '50c75a43-9502-4a81-919c-9402fdea1be8';
