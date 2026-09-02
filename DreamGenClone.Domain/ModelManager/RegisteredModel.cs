namespace DreamGenClone.Domain.ModelManager;

public sealed class RegisteredModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProviderId { get; set; } = string.Empty;
    public string ModelIdentifier { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ModelKind ModelKind { get; set; } = ModelKind.Text;
    public string? ImageSizeSupported { get; set; }
    public SceneImageModelFamily SceneImageModelFamily { get; set; }
    public SceneImagePromptDialect PromptDialect { get; set; }

    /// <summary>Whether this model accepts image content in multimodal completion requests.</summary>
    public bool SupportsImageInput { get; set; }

    public int? MaximumInputImages { get; set; }
    public long? MaximumInputImageBytes { get; set; }
    public long? MaximumInputImagePixels { get; set; }
    public int? MaximumInputImageDimension { get; set; }
    public string? AcceptedInputMediaTypes { get; set; }
    public long? MaximumResponseBytes { get; set; }
    public string? RuntimeRevision { get; set; }
    public string? ArtifactRevision { get; set; }

    /// <summary>Qwen diffusion model artifact for source-image editing.</summary>
    public string? ImageEditorDiffusionModel { get; set; }

    /// <summary>Qwen text encoder artifact for source-image editing.</summary>
    public string? ImageEditorTextEncoder { get; set; }

    /// <summary>Qwen VAE artifact for source-image editing.</summary>
    public string? ImageEditorVae { get; set; }

    public int? ImageEditorSteps { get; set; }
    public double? ImageEditorCfg { get; set; }
    public string? ImageEditorSampler { get; set; }
    public string? ImageEditorScheduler { get; set; }
    public double? ImageEditorDenoise { get; set; }
    public double? ImageEditorAuraFlowShift { get; set; }
    public double? ImageEditorCfgNormStrength { get; set; }

    /// <summary>Identity conditioning mechanism for the controlled render path ("IpAdapter" or "PuLid").
    /// Empty = identity rendering not configured (fails fast when requested).</summary>
    public string? IdentityMechanism { get; set; }

    /// <summary>Identity adapter strength (IP-Adapter weight / PuLID weight). Required when mechanism is set.</summary>
    public double? IdentityStrength { get; set; }

    /// <summary>IP-Adapter preset (e.g. "PLUS FACE (portraits)") or PuLID file
    /// (e.g. "ip-adapter_pulid_sdxl_fp16.safetensors"). Required when mechanism is set.</summary>
    public string? IdentityAdapterRef { get; set; }

    /// <summary>CLIP vision model file for IP-Adapter (e.g. "CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors").
    /// Null for PuLID (EVA02-CLIP is loaded by the node).</summary>
    public string? IdentityClipVisionRef { get; set; }

    public bool IsEnabled { get; set; } = true;
    /// <summary>Whether this model's chat template supports chat_template_kwargs.thinking.</summary>
    public bool SupportsThinkingControl { get; set; }
    public StructuredOutputMode StructuredOutputMode { get; set; }
    public int? MaximumContextTokens { get; set; }
    public int? MaximumOutputTokens { get; set; }
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>Context window size in tokens (e.g. 4096, 8192, 32768, 131072). 0 = unknown.</summary>
    public int ContextWindowSize { get; set; }

    /// <summary>Quantization level if applicable (e.g., "Q4_K_M", "Q8_0", "FP16", "FP32", ""). Empty = unknown/full-precision.</summary>
    public string Quantization { get; set; } = string.Empty;

    /// <summary>Approximate parameter count (e.g., "7B", "13B", "70B", "8x7B"). Empty = unknown.</summary>
    public string ParameterCount { get; set; } = string.Empty;

    /// <summary>Free-text notes about this model (e.g., fine-tune details, known strengths/weaknesses, special tokens).</summary>
    public string? Notes { get; set; }

    /// <summary>Provider name, populated from JOIN queries (not persisted separately).</summary>
    public string? ProviderName { get; set; }
}
