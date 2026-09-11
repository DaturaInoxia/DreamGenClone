UPDATE RegisteredModels
SET SupportedIdentityStrategiesJson = '["ReferenceConditioning"]',
    SupportedVisualStrategiesJson = '["ReferenceConditioning"]',
    CapabilityQualificationsJson = '[{"Strategy":"ReferenceConditioning","EndpointId":"' || (SELECT Id FROM Providers WHERE Name = 'RunPod Serverless BigLust') || '","Qualified":true,"ProofId":"2026-09-02_132309-deanv6-front"}]',
    Notes = CASE
        WHEN Notes IS NULL OR TRIM(Notes) = '' THEN 'IP-Adapter PLUS FACE reference conditioning is declared AND qualified (proof 2026-09-02_132309-deanv6-front).'
        ELSE Notes || ' IP-Adapter PLUS FACE reference conditioning declared + qualified (proof 2026-09-02_132309-deanv6-front).'
    END
WHERE Id = '9142a254-2779-4002-9e18-2d796e1e9fbd'
  AND ModelIdentifier = 'bigLust_v16.safetensors'
  AND IdentityMechanism = 'IpAdapter'
  AND IdentityAdapterRef = 'PLUS FACE (portraits)'
  AND SupportedIdentityStrategiesJson = '[]'
  AND SupportedVisualStrategiesJson = '[]';