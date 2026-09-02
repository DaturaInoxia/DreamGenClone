namespace DreamGenClone.Domain.ModelManager;

public enum SceneImageModelFamily
{
    Unknown = 0,
    Pony = 1,
    Sdxl = 2,
    Api = 3
}

public enum SceneImagePromptDialect
{
    Unknown = 0,
    PonyV6Tags = 1,
    SdxlNaturalLanguage = 2,
    NaturalLanguage = 3
}

public static class SceneImagePromptMetadata
{
    public static bool IsCompatible(SceneImageModelFamily family, SceneImagePromptDialect dialect) =>
        (family, dialect) is
            (SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags)
            or (SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage)
            or (SceneImageModelFamily.Api, SceneImagePromptDialect.NaturalLanguage);

    public static bool IsUnconfigured(SceneImageModelFamily family, SceneImagePromptDialect dialect) =>
        family == SceneImageModelFamily.Unknown && dialect == SceneImagePromptDialect.Unknown;
}