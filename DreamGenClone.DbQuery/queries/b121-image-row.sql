-- Read the SceneImages row the UI shows as a stuck "Edit identity" item.
SELECT Id, SessionId, InteractionId, BeatId, Operation, Status, ProductionStage, Disposition,
       SourceImageId, EditSessionId, EditCompilationAttemptId, EditPromptRevisionId,
       FileRelativePath, Sha256, ModelIdentifier, ProviderName, RequestedModelId,
       IdentityPackId, IdentityStale, ErrorMessage,
       CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
FROM SceneImages
WHERE Id = '{{id}}';
