using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The dataset's declared family and the profile's qualified family are compared as exact strings, so the list of
/// legal values is pinned rather than trusted. Every value must name a real <see cref="SceneImageModelFamily"/> that
/// a LoRA can be trained for — a family spelled some other way matches no profile, which is how a dataset ends up
/// declaring "IDK" and looking perfectly healthy.
/// </summary>
public sealed class CharacterLoraModelFamiliesTests
{
    [Fact]
    public void EveryFamilyNamesATrainableSceneImageModelFamily()
    {
        Assert.NotEmpty(CharacterLoraModelFamilies.All);
        Assert.Equal(
            CharacterLoraModelFamilies.All.Count,
            CharacterLoraModelFamilies.All.Distinct(StringComparer.Ordinal).Count());

        foreach (var family in CharacterLoraModelFamilies.All)
        {
            Assert.True(
                Enum.TryParse<SceneImageModelFamily>(family, ignoreCase: false, out var parsed),
                $"'{family}' is not a SceneImageModelFamily member.");
            Assert.NotEqual(SceneImageModelFamily.Unknown, parsed);
            Assert.NotEqual(SceneImageModelFamily.Api, parsed);
        }
    }

    [Fact]
    public void UnknownAndApiAreNeverOffered()
    {
        Assert.DoesNotContain(nameof(SceneImageModelFamily.Unknown), CharacterLoraModelFamilies.All);
        Assert.DoesNotContain(nameof(SceneImageModelFamily.Api), CharacterLoraModelFamilies.All);
    }

    [Fact]
    public void OnlyTheExactSpellingOfAnOfferedFamilyIsKnown()
    {
        Assert.True(CharacterLoraModelFamilies.IsKnown(nameof(SceneImageModelFamily.QwenImage21)));
        Assert.True(CharacterLoraModelFamilies.IsKnown($"  {nameof(SceneImageModelFamily.Sdxl)}  "));

        // The typo that started this: a hand-typed placeholder, and a cased variant of a real family.
        Assert.False(CharacterLoraModelFamilies.IsKnown("IDK"));
        Assert.False(CharacterLoraModelFamilies.IsKnown("sdxl"));
        Assert.False(CharacterLoraModelFamilies.IsKnown("SDXL"));
        Assert.False(CharacterLoraModelFamilies.IsKnown(null));
        Assert.False(CharacterLoraModelFamilies.IsKnown("   "));
    }

    [Fact]
    public void TheRefusalNamesEveryFamilyThatWouldHaveWorked()
    {
        var message = CharacterLoraModelFamilies.DescribeRefusal("IDK", "LoRA dataset target");

        Assert.Contains("IDK", message, StringComparison.Ordinal);
        Assert.Contains("LoRA dataset target", message, StringComparison.Ordinal);
        foreach (var family in CharacterLoraModelFamilies.All)
        {
            Assert.Contains(family, message, StringComparison.Ordinal);
        }
    }
}
