using DreamGenClone.Web.Application.RolePlay.Editing;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The scene-LoRA selection's wire form, which BOTH stores write and ONE handler reads (B-143). It is pinned here
/// because the two halves drifted apart once already (a serializer in the scene service, a reader in the handler), and
/// because the failure that matters is the quiet one: a selection that cannot be read must FAIL the run, never be
/// treated as "no LoRAs".
/// </summary>
public sealed class SceneLoraSelectionWireTests
{
    [Fact]
    public void Serialize_WithNoSelection_ReturnsNull()
    {
        Assert.Null(SceneLoraSelectionWire.Serialize(null));
        Assert.Null(SceneLoraSelectionWire.Serialize([]));
    }

    [Fact]
    public void Serialize_ThenRead_RoundTripsTheStackInOrder()
    {
        var json = SceneLoraSelectionWire.Serialize(
        [
            new SceneImageLoraSelection { FileName = "thesealpacas_qwen21_nsfw.safetensors", Strength = 0.8 },
            new SceneImageLoraSelection { FileName = "missionary_lokr.safetensors", Strength = 0.6 }
        ]);

        var selections = SceneLoraSelectionWire.Read(json);

        Assert.Collection(
            selections,
            first =>
            {
                Assert.Equal("thesealpacas_qwen21_nsfw.safetensors", first.FileName);
                Assert.Equal(0.8, first.Strength!.Value);
            },
            second => Assert.Equal("missionary_lokr.safetensors", second.FileName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_WithoutAStack_IsNoSelectionRatherThanAFailure(string? json)
    {
        Assert.Empty(SceneLoraSelectionWire.Read(json));
    }

    /// <summary>
    /// A payload that names a stack but cannot be parsed fails the run: rendering without the picked LoRAs produces an
    /// image nobody asked for, and it is indistinguishable from a render that applied them.
    /// </summary>
    [Theory]
    [InlineData("{\"fileName\":")]
    [InlineData("null")]
    public void Read_WithAMalformedStack_Throws(string json)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SceneLoraSelectionWire.Read(json));

        Assert.Contains("could not be read", error.Message, StringComparison.Ordinal);
    }
}
