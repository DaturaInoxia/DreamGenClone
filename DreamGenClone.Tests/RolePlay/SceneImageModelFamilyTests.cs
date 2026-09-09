using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageModelFamilyTests
{
    [Theory]
    [InlineData(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags)]
    [InlineData(SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage)]
    [InlineData(SceneImageModelFamily.Flux, SceneImagePromptDialect.FluxNaturalLanguage)]
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
    [InlineData(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags, SceneImagePromptStyle.PonyV6Tags)]
    [InlineData(SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage, SceneImagePromptStyle.NaturalLanguage)]
    [InlineData(SceneImageModelFamily.Flux, SceneImagePromptDialect.FluxNaturalLanguage, SceneImagePromptStyle.NaturalLanguage)]
    [InlineData(SceneImageModelFamily.Api, SceneImagePromptDialect.NaturalLanguage, SceneImagePromptStyle.NaturalLanguage)]
    public void PromptStyleResolver_MapsFamilyDialectToStyle(
        SceneImageModelFamily family,
        SceneImagePromptDialect dialect,
        SceneImagePromptStyle expected)
    {
        Assert.Equal(expected, SceneImagePromptStyleResolver.FromFamilyDialect(family, dialect));
    }

    [Theory]
    [InlineData(SceneImagePromptStyle.Unknown, SceneImagePromptStyle.NaturalLanguage)]
    [InlineData(SceneImagePromptStyle.NaturalLanguage, SceneImagePromptStyle.NaturalLanguage)]
    [InlineData(SceneImagePromptStyle.PonyV6Tags, SceneImagePromptStyle.PonyV6Tags)]
    public void PromptStyleResolver_Effective_NormalizesUnknownToNaturalLanguage(
        SceneImagePromptStyle style,
        SceneImagePromptStyle expected)
    {
        Assert.Equal(expected, SceneImagePromptStyleResolver.Effective(style));
    }
}
