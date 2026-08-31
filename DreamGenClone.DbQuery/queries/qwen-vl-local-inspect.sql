-- Inspect the Qwen VL image compiler provider/model/function-default rows
-- for the local LM Studio re-point. Read-only.

SELECT 'PROVIDER' AS section, Id, Name, BaseUrl, ChatCompletionsPath, ReadinessPath,
       ReadinessSuccessContractJson, ServerIdentityPolicyJson, LifecycleStrategyIdentifier,
       CredentialReference, TimeoutSeconds, TransitionTimeoutSeconds, TransitionMarginSeconds,
       MaximumActiveRequests, QueueCapacity, IsEnabled, ImageCapability, ContentPolicy, ImageProtocol
FROM Providers
WHERE Id = '2dde3563-589d-436a-bc60-d646a2da3c25';

SELECT 'MODEL' AS section, Id, ProviderId, DisplayName, ModelIdentifier, IsEnabled,
       SupportsImageInput, AcceptedInputMediaTypes, MaximumInputImages,
       MaximumInputImageBytes, MaximumInputImagePixels, MaximumInputImageDimension,
       MaximumResponseBytes, RuntimeRevision, ArtifactRevision
FROM RegisteredModels
WHERE Id = 'db602892-d604-40b1-8f7d-7d6073f7fe1d';

SELECT 'FUNCTION_DEFAULTS' AS section, FunctionName, ModelId, Temperature, TopP, MaxTokens
FROM FunctionModelDefaults
WHERE FunctionName IN ('RolePlaySceneImageEditPromptCompiler', 'RolePlaySceneImageValidator');
