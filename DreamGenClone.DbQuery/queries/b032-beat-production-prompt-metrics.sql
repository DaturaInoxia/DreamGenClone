SELECT p.Version,
       p.Status AS PlanStatus,
       a.AttemptNumber,
       a.Status AS AttemptStatus,
       length(a.SystemPrompt) AS SystemChars,
       length(a.UserPrompt) AS UserChars,
       a.InputCharacters,
       a.PromptUtf8Bytes,
       a.PromptBuildDurationMs,
       a.QueueWaitMs,
       a.ProviderHeadersWaitMs,
       a.ResponseBodyReadMs,
       a.ProviderJsonDeserializationMs,
       a.ValidationDurationMs,
       a.OutputCharacters,
       a.CreatedUtc,
       a.StartedUtc,
       a.CompletedUtc
FROM SceneBeatProductionPlans p
JOIN SceneBeatCatalogues c ON c.Id = p.CatalogueId
JOIN SceneBeatProductionAttempts a ON a.OwnerRecordId = p.Id
WHERE c.SessionId LIKE '{{id}}%'
  AND p.BeatId = 'b1'
ORDER BY p.Version DESC, a.AttemptNumber DESC;
