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
    QwenImage21 = 5
}

public enum SceneImagePromptDialect
{
    Unknown = 0,
    PonyV6Tags = 1,
    SdxlNaturalLanguage = 2,
    NaturalLanguage = 3,
    FluxNaturalLanguage = 4
}

public static class SceneImagePromptMetadata
{
    public static bool IsCompatible(SceneImageModelFamily family, SceneImagePromptDialect dialect) =>
        (family, dialect) is
            (SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags)
            or (SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage)
            or (SceneImageModelFamily.Api, SceneImagePromptDialect.NaturalLanguage)
            or (SceneImageModelFamily.Flux, SceneImagePromptDialect.FluxNaturalLanguage)
            or (SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);

    public static bool IsUnconfigured(SceneImageModelFamily family, SceneImagePromptDialect dialect) =>
        family == SceneImageModelFamily.Unknown && dialect == SceneImagePromptDialect.Unknown;
}