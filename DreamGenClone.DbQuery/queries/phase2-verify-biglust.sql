SELECT p.Id AS ProfileId, p.Operation AS ProfileOperation, p.Status AS ProfileStatus, p.Enabled, c.Id AS CellId, c.Status AS CellStatus
FROM MediaCapabilityProfiles p
JOIN MediaCapabilityCells c ON c.CapabilityProfileId = p.Id
WHERE p.Id = 'phase2-biglust-v16-generate' AND c.Id = 'phase2-biglust-v16-one-actor';