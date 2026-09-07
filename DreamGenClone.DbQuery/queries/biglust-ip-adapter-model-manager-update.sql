UPDATE RegisteredModels
SET SupportedIdentityStrategiesJson = '["ReferenceConditioning"]',
    SupportedVisualStrategiesJson = '["ReferenceConditioning"]',
    Notes = CASE
        WHEN Notes IS NULL OR TRIM(Notes) = '' THEN 'IP-Adapter PLUS FACE is configured for reference conditioning. Declaration added 2026-09-06; qualification evidence is required before production routing can use it.'
        ELSE Notes || ' IP-Adapter PLUS FACE reference-conditioning declaration added 2026-09-06; qualification evidence is required before production routing can use it.'
    END
WHERE Id = '9142a254-2779-4002-9e18-2d796e1e9fbd'
  AND ModelIdentifier = 'bigLust_v16.safetensors'
  AND IdentityMechanism = 'IpAdapter'
  AND IdentityAdapterRef = 'PLUS FACE (portraits)'
  AND SupportedIdentityStrategiesJson = '[]'
  AND SupportedVisualStrategiesJson = '[]';