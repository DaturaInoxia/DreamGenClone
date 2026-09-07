SELECT 'ProductionAttempts' AS Source,
       Id,
       Status,
       CreatedUtc,
       CompletedUtc AS UpdatedUtc,
       ProviderKey AS Provider,
       NULL AS Endpoint,
       ProviderRequestId AS JobId,
       FailureDiagnostic AS Error
FROM ProductionAttempts
WHERE CreatedUtc >= '2026-09-06T03:54:49Z'
UNION ALL
SELECT 'ProducedImages' AS Source,
       Id,
       Status,
       CreatedUtc,
       UpdatedUtc,
       ModelId AS Provider,
       EndpointId AS Endpoint,
       NULL AS JobId,
       StoragePath AS Error
FROM ProducedImages
WHERE CreatedUtc >= '2026-09-06T03:54:49Z'
ORDER BY CreatedUtc;