SELECT Id, DisplayName, ModelIdentifier, ProviderId, IsEnabled FROM RegisteredModels WHERE lower(DisplayName) LIKE '%qwen%' OR lower(ModelIdentifier) LIKE '%qwen%' ORDER BY DisplayName;
