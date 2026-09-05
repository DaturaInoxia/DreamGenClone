SELECT p.Version,
       p.Status AS PlanStatus,
       a.Id AS AttemptId,
       a.Status AS AttemptStatus,
       a.SystemPrompt,
       a.UserPrompt,
       a.SystemPromptCharacters,
       a.UserPromptCharacters,
       a.InputCharacters,
       a.PromptUtf8Bytes,
       a.ProviderHeadersWaitMs,
       a.ResponseBodyReadMs,
       a.ValidationCode,
       a.CreatedUtc,
       a.StartedUtc,
       a.CompletedUtc
FROM SceneBeatProductionPlans p
JOIN SceneBeatCatalogues c ON c.Id = p.CatalogueId
JOIN SceneBeatProductionAttempts a ON a.OwnerRecordId = p.Id
WHERE c.SessionId LIKE '{{id}}%'
  AND p.BeatId = 'b1'
ORDER BY p.Version DESC, a.AttemptNumber DESC;
