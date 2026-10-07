using System.Text.Json;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

public sealed class B148DomainModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Setting_WorldLocation_DeserializesNullFromAPayloadThatLacksIt()
    {
        var setting = JsonSerializer.Deserialize<Setting>("{\"WorldDescription\":\"A trailer park at dusk\"}", JsonOptions);

        Assert.NotNull(setting);
        Assert.Null(setting!.WorldLocation);
    }

    [Fact]
    public void Setting_WorldLocation_RoundTripsWhenPresent()
    {
        var setting = JsonSerializer.Deserialize<Setting>(
            "{\"WorldLocation\":{\"AssetContainerId\":\"world-1\",\"RenderingDescription\":\"A weathered trailer park\"}}",
            JsonOptions);

        Assert.NotNull(setting);
        Assert.NotNull(setting!.WorldLocation);
        Assert.Equal("world-1", setting.WorldLocation!.AssetContainerId);
        Assert.Equal("A weathered trailer park", setting.WorldLocation.RenderingDescription);
    }

    [Fact]
    public void LocationBackdrop_CreateRequiresTheImageFields()
    {
        Assert.Throws<InvalidOperationException>(
            () => DreamGenClone.Domain.RolePlay.SceneImageLocationBackdrop.Create("loc-a", "img-a", string.Empty, 1, "Front"));
        Assert.Throws<InvalidOperationException>(
            () => DreamGenClone.Domain.RolePlay.SceneImageLocationBackdrop.Create("loc-a", "img-a", "ABC", 1, string.Empty));
    }
}
