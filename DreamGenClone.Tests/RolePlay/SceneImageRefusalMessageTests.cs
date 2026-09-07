using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageRefusalMessageTests
{
    [Theory]
    [InlineData(SceneImageRefusalMode.EmptyOutput)]
    [InlineData(SceneImageRefusalMode.PolicyError)]
    public void ForUser_IncludesModelAndProvider(SceneImageRefusalMode mode)
    {
        var message = SceneImageRefusalMessage.ForUser("model-test", "provider-test", mode);

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Contains("model-test", message, StringComparison.Ordinal);
        Assert.Contains("provider-test", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ForUser_ThrowsForNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SceneImageRefusalMessage.ForUser("model-test", "provider-test", SceneImageRefusalMode.None));
    }
}
