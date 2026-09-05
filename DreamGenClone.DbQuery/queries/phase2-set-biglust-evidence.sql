UPDATE MediaCapabilityProfiles
SET EvidenceRunId = '2026-08-31_150605-identity',
    PayloadJson = json_set(PayloadJson, '$.evidenceRunId', '2026-08-31_150605-identity')
WHERE Id = 'phase2-biglust-v16-generate';

UPDATE MediaCapabilityCells
SET EvidenceRunId = '2026-08-31_150605-identity',
    PayloadJson = json_set(PayloadJson, '$.evidenceRunId', '2026-08-31_150605-identity')
WHERE Id = 'phase2-biglust-v16-one-actor';