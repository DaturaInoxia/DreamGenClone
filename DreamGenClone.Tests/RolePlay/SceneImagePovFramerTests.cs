using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Tests for CR-006 P5 — POV framing (<see cref="SceneImagePovFramer"/>).
/// </summary>
public sealed class SceneImagePovFramerTests
{
    private static SceneImageBeat MakeBeat() => new()
    {
        SchemaVersion = 3,
        BeatId = "b1",
        Location = "trailer bedroom",
        Characters =
        [
            new SceneImageBeatCharacter
            {
                Name = "Becky",
                Involvement = "active",
                PhysicalLocation = "trailer bedroom",
                Position = "bedroom mirror",
                Sightline = "toward the kitchen through the partly open door",
                VisibleCharacterNames = ["Dean"]
            },
            new SceneImageBeatCharacter
            {
                Name = "Dean",
                Involvement = "observer",
                PhysicalLocation = "trailer kitchen",
                Position = "kitchen counter",
                Sightline = "through the partly open bedroom door",
                VisibleCharacterNames = ["Becky"]
            }
        ]
    };

    [Fact]
    public void GetPovOptions_AlwaysIncludesOmniscient()
    {
        var options = SceneImagePovFramer.GetPovOptions([]);
        Assert.Contains(SceneImagePovFramer.Omniscient, options);
    }

    [Fact]
    public void GetPovOptions_IncludesParticipants()
    {
        var options = SceneImagePovFramer.GetPovOptions(["Becky", "Dean"]);
        Assert.Contains("Becky", options);
        Assert.Contains("Dean", options);
        Assert.Contains(SceneImagePovFramer.Omniscient, options);
    }

    [Fact]
    public void GetPovOptions_Deduplicates()
    {
        var options = SceneImagePovFramer.GetPovOptions(["Becky", "Becky", "Dean"]);
        Assert.Equal(3, options.Count); // Omniscient + Becky + Dean
    }

    [Fact]
    public void BuildFramingLine_Omniscient_WideShot()
    {
        var line = SceneImagePovFramer.BuildFramingLine(MakeBeat(), "Omniscient");
        Assert.Contains("external third-person", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFramingLine_Omniscient_HonorsAngleOverride()
    {
        var line = SceneImagePovFramer.BuildFramingLine(MakeBeat(), "Omniscient", "High wide angle looking down at the scene");
        Assert.Contains("high wide angle", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("frame the complete visible event", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFramingLine_CharacterPov_FromPerspective()
    {
        var line = SceneImagePovFramer.BuildFramingLine(MakeBeat(), "Dean");
        Assert.Contains("trailer kitchen", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dean's eye position", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kitchen counter", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("distant", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("partly open bedroom door", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Becky", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("first-person", line, StringComparison.OrdinalIgnoreCase);
        // Dean is a remote observer (kitchen vs act in bedroom) → off-camera, own body not visible.
        Assert.Contains("own body is not visible", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("off-camera;", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Exactly 1", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFramingLine_UnknownPov_FailsExplicitly()
    {
        Assert.Throws<InvalidOperationException>(() => SceneImagePovFramer.BuildFramingLine(MakeBeat(), ""));
        Assert.Throws<InvalidOperationException>(() => SceneImagePovFramer.BuildFramingLine(MakeBeat(), "Absent"));
    }
}