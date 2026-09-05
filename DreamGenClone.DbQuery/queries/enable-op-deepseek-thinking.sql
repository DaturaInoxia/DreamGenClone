UPDATE RegisteredModels
SET SupportsThinkingControl = 1
WHERE DisplayName = 'OP-deepseek-v4-flash-0731'
  AND ModelIdentifier = 'deepseek/deepseek-v4-flash-0731'
  AND IsEnabled = 1;
