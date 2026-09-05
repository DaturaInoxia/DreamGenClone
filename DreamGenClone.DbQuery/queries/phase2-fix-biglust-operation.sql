UPDATE MediaCapabilityProfiles
SET PayloadJson = json_set(PayloadJson, '$.operation', 1)
WHERE Id = 'phase2-biglust-v16-generate';