SELECT Id, SessionId, InteractionId, Status, ModelIdentifier, ErrorMessage,
       length(OutputPrompt) AS PromptLen, substr(OutputPrompt, 1, 120) AS PromptHead,
       CreatedUtc, UpdatedUtc
FROM SceneImagePrompts
WHERE SessionId LIKE '{{id}}%'
ORDER BY CreatedUtc DESC
LIMIT 12;
