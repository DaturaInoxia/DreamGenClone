SELECT
    m.Id, m.ProviderId, m.ModelIdentifier, m.DisplayName, m.IsEnabled,
    m.SupportsThinkingControl, m.ContextWindowSize, m.Quantization, m.ParameterCount,
    m.Notes, m.ModelKind, m.ImageSizeSupported, m.SupportsImageInput,
    m.MaximumInputImages, m.MaximumInputImageBytes, m.MaximumInputImagePixels,
    m.MaximumInputImageDimension, m.AcceptedInputMediaTypes, m.MaximumResponseBytes,
    m.RuntimeRevision, m.ArtifactRevision, m.ImageEditorDiffusionModel,
    m.ImageEditorTextEncoder, m.ImageEditorVae, m.ImageEditorSteps, m.ImageEditorCfg,
    m.ImageEditorSampler, m.ImageEditorScheduler, m.ImageEditorDenoise,
    m.ImageEditorAuraFlowShift, m.ImageEditorCfgNormStrength,
    m.IdentityMechanism, m.IdentityStrength, m.IdentityAdapterRef, m.IdentityClipVisionRef,
    m.CreatedUtc
FROM RegisteredModels m
WHERE m.Id = '74208319-2895-4fd9-b231-c2eaf3329429';
