using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// A multi-angle camera pose is assembled deterministically from the editor LoRA's <c>&lt;sks&gt;</c> grammar, so the
/// one thing that can go wrong is the assembly: a token that drifts, an undefined pose, or a checksum that does not
/// match. Each of those sends an instruction that looks plausible and is wrong, so each is refused or pinned here.
/// </summary>
public sealed class MultiAngleCameraInstructionComposerTests
{
    [Fact]
    public void Compose_DefaultPose_IsTheExactSksGrammar()
        => Assert.Equal("<sks> front view eye-level shot medium shot", MultiAngleCameraInstructionComposer.Compose(
            MultiAngleAzimuth.Front, MultiAngleElevation.EyeLevel, MultiAngleDistance.Medium));

    [Fact]
    public void Compose_BackWideHighAngle_IsTheExactSksGrammar()
        => Assert.Equal("<sks> back view high-angle shot wide shot", MultiAngleCameraInstructionComposer.Compose(
            MultiAngleAzimuth.Back, MultiAngleElevation.HighAngle, MultiAngleDistance.Wide));

    [Theory]
    [InlineData(MultiAngleAzimuth.Front, "front view")]
    [InlineData(MultiAngleAzimuth.FrontRight, "front-right quarter view")]
    [InlineData(MultiAngleAzimuth.RightSide, "right side view")]
    [InlineData(MultiAngleAzimuth.BackRight, "back-right quarter view")]
    [InlineData(MultiAngleAzimuth.Back, "back view")]
    [InlineData(MultiAngleAzimuth.BackLeft, "back-left quarter view")]
    [InlineData(MultiAngleAzimuth.LeftSide, "left side view")]
    [InlineData(MultiAngleAzimuth.FrontLeft, "front-left quarter view")]
    public void AzimuthToken_IsTheLoRasFixedVocabulary(MultiAngleAzimuth azimuth, string token)
        => Assert.Equal(token, MultiAngleCameraTokens.Azimuth(azimuth));

    [Theory]
    [InlineData(MultiAngleElevation.LowAngle, "low-angle shot")]
    [InlineData(MultiAngleElevation.EyeLevel, "eye-level shot")]
    [InlineData(MultiAngleElevation.Elevated, "elevated shot")]
    [InlineData(MultiAngleElevation.HighAngle, "high-angle shot")]
    public void ElevationToken_IsTheLoRasFixedVocabulary(MultiAngleElevation elevation, string token)
        => Assert.Equal(token, MultiAngleCameraTokens.Elevation(elevation));

    [Theory]
    [InlineData(MultiAngleDistance.CloseUp, "close-up")]
    [InlineData(MultiAngleDistance.Medium, "medium shot")]
    [InlineData(MultiAngleDistance.Wide, "wide shot")]
    public void DistanceToken_IsTheLoRasFixedVocabulary(MultiAngleDistance distance, string token)
        => Assert.Equal(token, MultiAngleCameraTokens.Distance(distance));

    [Fact]
    public void Compose_EveryPoseOfThe96Grid_StartsWithTheTriggerAndHasNoBraces()
    {
        foreach (var azimuth in Enum.GetValues<MultiAngleAzimuth>())
        foreach (var elevation in Enum.GetValues<MultiAngleElevation>())
        foreach (var distance in Enum.GetValues<MultiAngleDistance>())
        {
            var instruction = MultiAngleCameraInstructionComposer.Compose(azimuth, elevation, distance);
            Assert.StartsWith("<sks>", instruction, StringComparison.Ordinal);
            Assert.DoesNotContain("{", instruction, StringComparison.Ordinal);
            Assert.DoesNotContain("}", instruction, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Compose_AnUndefinedAzimuth_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => MultiAngleCameraInstructionComposer.Compose(
            (MultiAngleAzimuth)17, MultiAngleElevation.EyeLevel, MultiAngleDistance.Medium));
        Assert.Contains("azimuth", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_AnUndefinedElevation_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => MultiAngleCameraInstructionComposer.Compose(
            MultiAngleAzimuth.Front, (MultiAngleElevation)99, MultiAngleDistance.Medium));
        Assert.Contains("elevation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_AnUndefinedDistance_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => MultiAngleCameraInstructionComposer.Compose(
            MultiAngleAzimuth.Front, MultiAngleElevation.EyeLevel, (MultiAngleDistance)42));
        Assert.Contains("distance", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The instruction checksum is the same regardless of who computes it, so a queued row and its re-derivation
    /// agree by construction.</summary>
    [Fact]
    public void Provenance_Checksum_MatchesTheComposedInstruction()
    {
        var instruction = MultiAngleCameraInstructionComposer.Compose(
            MultiAngleAzimuth.LeftSide, MultiAngleElevation.LowAngle, MultiAngleDistance.CloseUp);
        Assert.Equal(
            MediaEditMultiAngleProvenance.InstructionSha256(instruction),
            MediaEditMultiAngleProvenance.InstructionSha256(instruction));
    }

    /// <summary>An instruction checksum that is not a SHA-256 value cannot prove the instruction that will run.</summary>
    [Fact]
    public void Provenance_ANonShaChecksum_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => MediaEditMultiAngleProvenance.Validate(
            new MediaEditMultiAngleInstruction(
                MultiAngleAzimuth.Front, MultiAngleElevation.EyeLevel, MultiAngleDistance.Medium, "not-a-hash")));
        Assert.Contains("SHA-256", error.Message, StringComparison.Ordinal);
    }
}
