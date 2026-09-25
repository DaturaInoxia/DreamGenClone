-- The provider endpoint and readiness contract behind the multimodal compiler model: the identity the app REQUIRES
-- (ModelIdentifier) versus the path it probes (ReadinessPath / ChatCompletionsPath) is what the failing calls are
-- measured against.
SELECT rm.Id AS ModelId, rm.ModelIdentifier, rm.DisplayName, p.Name AS ProviderName, p.BaseUrl,
       p.ReadinessPath, p.ChatCompletionsPath, p.ReadinessSuccessContractJson,
       p.IsEnabled AS ProviderEnabled, rm.IsEnabled AS ModelEnabled
FROM RegisteredModels rm
JOIN Providers p ON p.Id = rm.ProviderId
WHERE rm.Id = '9f2c7a41-3b6d-4e08-95c1-a7d4e609b812';
