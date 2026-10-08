namespace DreamGenClone.Domain.ModelManager;

public enum SceneImageModelFamily
{
    Unknown = 0,
    Pony = 1,
    Sdxl = 2,
    Api = 3,
    Flux = 4,

    /// <summary>
    /// Qwen-Image-2.1: a unified 7B text-to-image + image-editing model (Qwen3-VL 8B text encoder,
    /// 64-channel RGBA VAE) that takes its inputs as native references through one autogrow input
    /// instead of a LoRA / IP-Adapter identity mechanism. Appended, never renumbered - the enum
    /// integer is persisted and the DbQuery transfer table indexes names by value.
    /// </summary>
    QwenImage21 = 5,

    /// <summary>
    /// Krea 2 (Krea-2 Turbo): a 12B dense diffusion transformer trained from scratch, with a Qwen3-VL 4B
    /// text encoder and the Qwen Image VAE. Plain text-to-image with no reference conditioning, no edit
    /// path and no ControlNet - the only conditioning is text plus LoRAs. Its graph is split
    /// (UNETLoader + CLIPLoader type <c>krea2</c> + VAELoader), its sampler envelope is a cfg-1 distilled
    /// 8-step recipe, and its negative is a <c>ConditioningZeroOut</c> of the positive (it takes no
    /// negative text at all). Appended, never renumbered - the enum integer is persisted and the DbQuery
    /// transfer table indexes names by value.
    /// </summary>
    /// <summary>
    /// Krea 2 (Krea-2 Turbo): a 12B dense diffusion transformer trained from scratch, with a Qwen3-VL 4B
    /// text encoder and the Qwen Image VAE. Plain text-to-image with no reference conditioning, no edit
    /// path and no ControlNet - the only conditioning is text plus LoRAs. Its graph is split
    /// (UNETLoader + CLIPLoader type <c>krea2</c> + VAELoader), its sampler envelope is a cfg-1 distilled
    /// 8-step recipe, and its negative is a <c>ConditioningZeroOut</c> of the positive (it takes no
    /// negative text at all). Appended, never renumbered - the enum integer is persisted and the DbQuery
    /// transfer table indexes names by value.
    /// </summary>
    Krea2 = 6,

    /// <summary>
    /// MiniMax H3 Ref2VA: a local video model (pruned diffusion transformer + a quantized 32B text encoder +
    /// separate video and audio VAEs) driven by <c>MiniMaxH3ReferenceToVideo</c>, which conditions on ordered
    /// reference images and produces picture and audio in one pass. Its prompt is the six-section H3 document
    /// rather than a tag list, and its graph decodes and muxes audio, so it is a video family and not an image
    /// one. Appended, never renumbered - the enum integer is persisted and the DbQuery transfer table indexes
    /// names by value.
    /// </summary>
    MiniMaxH3Ref2VA = 7
}

public enum SceneImagePromptDialect
{
    Unknown = 0,
    PonyV6Tags = 1,
    SdxlNaturalLanguage = 2,
    NaturalLanguage = 3,
    FluxNaturalLanguage = 4,

    /// <summary>
    /// Krea 2's own dialect. It reads the same photographic-brief shape as the natural-language families, but it
    /// is a DISTINCT value because its compiler carries rules the others do not (a photographic brief, never a
    /// body noun-list; no framing demands; no face-facing clause on act prompts; both actors of an act named).
    /// Appended, never renumbered.
    /// </summary>
    Krea2NaturalLanguage = 5,

    /// <summary>
    /// MiniMax H3's own dialect: the six-section reference-conditioning document (subject_definitions, summary,
    /// retention_analysis, detailed_description, overall_soundscape, non_diegetic_music) with fixed retention
    /// tokens and <c>&lt;Picture i&gt;</c> / <c>&lt;Video k&gt;</c> / <c>&lt;Audio j&gt;</c> labels that must match the
    /// node's reference slot order. A distinct value because it is a structured document, not natural language
    /// prose that happens to mention its references. Appended, never renumbered.
    /// </summary>
    MiniMaxH3SixSection = 6
}

public static class SceneImagePromptMetadata
{
    public static bool IsCompatible(SceneImageModelFamily family, SceneImagePromptDialect dialect) =>
        (family, dialect) is
            (SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags)
            or (SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage)
            or (SceneImageModelFamily.Api, SceneImagePromptDialect.NaturalLanguage)
            or (SceneImageModelFamily.Flux, SceneImagePromptDialect.FluxNaturalLanguage)
            or (SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage)
            or (SceneImageModelFamily.Krea2, SceneImagePromptDialect.Krea2NaturalLanguage)
            or (SceneImageModelFamily.MiniMaxH3Ref2VA, SceneImagePromptDialect.MiniMaxH3SixSection);

    public static bool IsUnconfigured(SceneImageModelFamily family, SceneImagePromptDialect dialect) =>
        family == SceneImageModelFamily.Unknown && dialect == SceneImagePromptDialect.Unknown;
}