using System.Text.Json;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The read-back a surface uses to re-apply what made an image — the "reload" half of the card's reload command.
///
/// <para>
/// Two things are pinned. What the row recorded comes back exactly (the LoRA stack, the pose skeleton and the strength
/// it conditioned at). And what the row did NOT record comes back empty or null rather than as a value from
/// <see cref="SceneImageStudioSettings"/>'s own defaults — a settings object that was never named on the row must never
/// become a claim about a render that already happened.
/// </para>
/// </summary>
public sealed class SceneImageRecordedInputsTests
{
    /// <summary>
    /// The settings blob is written by the page with the default serializer, so the round-trip read must accept exactly
    /// what that writer produces — this test builds the JSON the same way the composer and the studio do.
    /// </summary>
    [Fact]
    public void Read_ReturnsTheLorasAndPoseTheRowRecorded()
    {
        var settings = new SceneImageStudioSettings
        {
            CharacterLoras =
            [
                new SceneImageCharacterLoraSelection { ArtifactId = "artifact-becky-v1", Strength = 0.8 }
            ],
            SceneLoras =
            [
                new SceneImageLoraSelection { FileName = "krea2_nsfw_v4_v43exp.safetensors", Strength = 0.7 }
            ],
            PoseReference = new SceneImagePoseReference { StoragePath = "skeletons/pose-7.png", Strength = 0.75 }
        };

        var recorded = SceneImageRecordedInputs.Read(JsonSerializer.Serialize(settings));

        var characterLora = Assert.Single(recorded.CharacterLoras);
        Assert.Equal("artifact-becky-v1", characterLora.ArtifactId);
        Assert.Equal(0.8, characterLora.Strength);

        var sceneLora = Assert.Single(recorded.SceneLoras);
        Assert.Equal("krea2_nsfw_v4_v43exp.safetensors", sceneLora.FileName);
        Assert.Equal(0.7, sceneLora.Strength);

        Assert.Equal("skeletons/pose-7.png", recorded.PoseSkeletonRelativePath);
        Assert.Equal(0.75, recorded.PoseStrength);
    }

    /// <summary>
    /// The reason this reads JSON instead of deserializing the settings type: that type carries defaults for the
    /// sampler, the scheduler, CFG, the step count and the image size, so a row that named none of them would still
    /// deserialize into a fully populated object. A settings snapshot holding ONLY those defaults therefore has to read
    /// back as "nothing recorded".
    /// </summary>
    [Fact]
    public void Read_DoesNotTurnTheSettingsTypesOwnDefaultsIntoRecordedInputs()
    {
        var defaultsOnly = JsonSerializer.Serialize(new SceneImageStudioSettings());

        // Guard the premise: the serialized blob really does carry the type's defaults.
        Assert.Contains("dpmpp_2m_sde", defaultsOnly, StringComparison.Ordinal);
        Assert.Contains("karras", defaultsOnly, StringComparison.Ordinal);

        var recorded = SceneImageRecordedInputs.Read(defaultsOnly);

        Assert.Empty(recorded.CharacterLoras);
        Assert.Empty(recorded.SceneLoras);
        Assert.Null(recorded.PoseSkeletonRelativePath);
        Assert.Null(recorded.PoseStrength);
    }

    /// <summary>
    /// A skeleton the row recorded without the strength it conditioned at: the caller gets the path and NO strength, so
    /// it can restore the pose and say the strength was not recorded, rather than applying one nobody chose.
    /// </summary>
    [Fact]
    public void Read_APoseWithNoRecordedStrength_KeepsTheStrengthNull()
    {
        var recorded = SceneImageRecordedInputs.Read(
            """{ "PoseReference": { "StoragePath": "skeletons/pose-9.png" } }""");

        Assert.Equal("skeletons/pose-9.png", recorded.PoseSkeletonRelativePath);
        Assert.Null(recorded.PoseStrength);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{ not json")]
    public void Read_WithNothingReadable_RecordsNothing(string? settingsJson)
    {
        var recorded = SceneImageRecordedInputs.Read(settingsJson);

        Assert.Empty(recorded.CharacterLoras);
        Assert.Empty(recorded.SceneLoras);
        Assert.Null(recorded.PoseSkeletonRelativePath);
        Assert.Null(recorded.PoseStrength);
    }

    [Fact]
    public void Read_APoseThatIsNotAnObject_ReadsNoPose()
    {
        var recorded = SceneImageRecordedInputs.Read(
            """{ "PoseReference": "skeletons/pose-9.png", "SceneLoras": [ { "FileName": "x.safetensors", "Strength": 0.5 } ] }""");

        Assert.Null(recorded.PoseSkeletonRelativePath);
        Assert.Single(recorded.SceneLoras);
    }
}
