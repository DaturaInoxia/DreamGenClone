SELECT Id, SessionId, ActiveThemeLabel, Phase, ModelIdentifier,
       Succeeded, ErrorMessage, SelectedDirectiveSummary,
       length(GenerationPrompt) AS PromptLen,
       length(GenerationResponse) AS RespLen,
       CreatedUtc, UpdatedUtc, StagedInteractionId, ContinuationInteractionId
FROM SteeringGenerationRecords
WHERE SessionId = '{{id}}'
ORDER BY CreatedUtc DESC;
