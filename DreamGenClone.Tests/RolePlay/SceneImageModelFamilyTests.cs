using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageModelFamilyTests
{
    [Theory]
    [InlineData(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags)]
    [InlineData(SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage)]
    public void IsCompatible_RegisteredPair_ReturnsTrue(
        SceneImageModelFamily family,
        SceneImagePromptDialect dialect)
    {
        Assert.True(SceneImagePromptMetadata.IsCompatible(family, dialect));
    }

    [Theory]
    [InlineData(SceneImageModelFamily.Pony, SceneImagePromptDialect.SdxlNaturalLanguage)]
    [InlineData(SceneImageModelFamily.Sdxl, SceneImagePromptDialect.PonyV6Tags)]
    [InlineData(SceneImageModelFamily.Unknown, SceneImagePromptDialect.PonyV6Tags)]
    [InlineData(SceneImageModelFamily.Pony, SceneImagePromptDialect.Unknown)]
    public void IsCompatible_UnregisteredPair_ReturnsFalse(
        SceneImageModelFamily family,
        SceneImagePromptDialect dialect)
    {
        Assert.False(SceneImagePromptMetadata.IsCompatible(family, dialect));
    }

    [Fact]
    public void IsUnconfigured_BothUnknown_ReturnsTrue()
    {
        Assert.True(SceneImagePromptMetadata.IsUnconfigured(
            SceneImageModelFamily.Unknown,
            SceneImagePromptDialect.Unknown));
    }

    [Theory]
    [InlineData(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags)]
    [InlineData(SceneImageModelFamily.Unknown, SceneImagePromptDialect.PonyV6Tags)]
    public void IsUnconfigured_AnyConfiguredValue_ReturnsFalse(
        SceneImageModelFamily family,
        SceneImagePromptDialect dialect)
    {
        Assert.False(SceneImagePromptMetadata.IsUnconfigured(family, dialect));
    }
}
