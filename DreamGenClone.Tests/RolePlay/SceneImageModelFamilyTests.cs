using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageModelFamilyTests
{
    [Theory]
    [InlineData("ponyDiffusionV6XL_v6.safetensors")]
    [InlineData("PonyDiffusionV6XL_v6.safetensors")]
    [InlineData("ponyxl.safetensors")]
    public void Classify_PonyIdentifier_ReturnsPony(string checkpoint)
    {
        Assert.Equal(SceneImageModelFamily.Pony, SceneImageModelFamilyResolver.Classify(checkpoint));
    }

    [Theory]
    [InlineData("juggernautXL_ragnarok.safetensors")]
    [InlineData("sd_xl_base_1.0.safetensors")]
    [InlineData("RealVisXL_V4.0.safetensors")]
    [InlineData("juggernautXL_ragnarok.safetensors")]
    [InlineData("lustifyNSFWCheckpoint_zenithV9.safetensors")]
    [InlineData("lustifyNSFWCheckpoint_apexV8.safetensors")]
    public void Classify_SdxlIdentifier_ReturnsSdxl(string checkpoint)
    {
        Assert.Equal(SceneImageModelFamily.Sdxl, SceneImageModelFamilyResolver.Classify(checkpoint));
    }

    [Theory]
    [InlineData("flux1-schnell-fp8.safetensors")]
    [InlineData("v1-5-pruned-emaonly.safetensors")]
    [InlineData("some-random-model.bin")]
    [InlineData("")]
    public void Classify_UnknownIdentifier_ReturnsUnknown(string checkpoint)
    {
        Assert.Equal(SceneImageModelFamily.Unknown, SceneImageModelFamilyResolver.Classify(checkpoint));
    }

    [Fact]
    public void Classify_Null_ReturnsUnknown()
    {
        Assert.Equal(SceneImageModelFamily.Unknown, SceneImageModelFamilyResolver.Classify(null));
    }
}
