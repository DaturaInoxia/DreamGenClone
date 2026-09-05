UPDATE MediaCapabilityProfiles
SET PayloadJson = json_set(PayloadJson, '$.operation', 1, '$.status', 2)
WHERE Id = 'phase2-biglust-v16-generate';

UPDATE MediaCapabilityCells
SET PayloadJson = json_set(PayloadJson, '$.status', 3)
WHERE Id = 'phase2-biglust-v16-one-actor';