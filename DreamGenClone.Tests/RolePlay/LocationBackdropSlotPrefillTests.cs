using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

public sealed class LocationBackdropSlotPrefillTests
{
    [Fact]
    public void For_FillsTheDeclaredLocationSlot()
    {
        var blueprint = ImageStepBlueprintFactory.ForPackIdentityComposition([]);
        var backdrop = SceneImageLocationBackdrop.Create("loc-a", "img-a", "ABC", 2, "Front");

        var binding = LocationBackdropSlotPrefill.For(blueprint, backdrop);

        Assert.Equal("Location", binding.ElementKey);
        Assert.Equal(ImageStepSlotKind.Location.ToString(), binding.Kind);
        Assert.Equal("loc-a", binding.SceneAssetId);
        Assert.Equal("img-a", binding.SceneAssetImageId);
        Assert.Equal("ABC", binding.SceneAssetSha256);
        Assert.Equal(2, binding.SceneAssetVersion);
    }

    [Fact]
    public void For_RefusesABlueprintWithoutALocationSlot()
    {
        // An edit step declares a face slot per actor and no location slot.
        var blueprint = ImageStepBlueprintFactory.ForEdit([new ImageStepActor("tpl-dean", "Dean")]);

        var backdrop = SceneImageLocationBackdrop.Create("loc-a", "img-a", "ABC", 2, "Front");

        var exception = Assert.Throws<InvalidOperationException>(
            () => LocationBackdropSlotPrefill.For(blueprint, backdrop));
        Assert.Contains("Location", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HasLocationSlot_ReportsTheDeclaredSlot()
    {
        Assert.True(LocationBackdropSlotPrefill.HasLocationSlot(ImageStepBlueprintFactory.ForPackIdentityComposition([])));
        Assert.False(LocationBackdropSlotPrefill.HasLocationSlot(
            ImageStepBlueprintFactory.ForEdit([new ImageStepActor("tpl-dean", "Dean")])));
    }
}
