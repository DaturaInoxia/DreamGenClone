using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The angle direction convention (B-121 FR21-011). The gate reads ONE number — the tool's signed nose
/// offset — and these tests pin that: the wrong-side / facing-camera blocks, the deliberate silence on
/// profiles, and the guarantee that iris/interocular values can never influence the verdict (they are
/// invalid under yaw, which is why the nose offset exists at all).
/// </summary>
public sealed class CharacterIdentityAngleYawGateTests
{
    private const double Deadband = 5.0;

    [Fact]
    public void ThreeQuarterLeft_NoseTowardTheLeftOfTheImage_Passes()
    {
        var verdict = CharacterIdentityAngleYawGate.Evaluate(
            CharacterIdentityAngleView.ThreeQuarterLeft, Measurement(noseOffset: -45.12), Deadband);

        Assert.True(verdict.Passed);
        Assert.True(verdict.Asserted);
        Assert.Null(verdict.BlockReason);
        Assert.Equal(-45.12, verdict.NoseOffsetPercent);
    }

    [Fact]
    public void ThreeQuarterRight_NoseTowardTheRightOfTheImage_Passes()
    {
        var verdict = CharacterIdentityAngleYawGate.Evaluate(
            CharacterIdentityAngleView.ThreeQuarterRight, Measurement(noseOffset: 27.70), Deadband);

        Assert.True(verdict.Passed);
        Assert.True(verdict.Asserted);
        Assert.Equal(27.70, verdict.NoseOffsetPercent);
    }

    [Fact]
    public void ThreeQuarterLeft_FacingTheCamera_IsBlockedAsUnturned_WithTheMeasuredValue()
    {
        var verdict = CharacterIdentityAngleYawGate.Evaluate(
            CharacterIdentityAngleView.ThreeQuarterLeft, Measurement(noseOffset: 2.4), Deadband);

        Assert.False(verdict.Passed);
        Assert.True(verdict.Asserted);
        Assert.NotNull(verdict.BlockReason);
        Assert.Contains("not turned", verdict.BlockReason!, StringComparison.Ordinal);
        Assert.Contains("image-left", verdict.BlockReason!, StringComparison.Ordinal);
        Assert.Contains("2.40", verdict.BlockReason!, StringComparison.Ordinal);
        Assert.Contains(Deadband.ToString("F1"), verdict.BlockReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeQuarterLeft_NoseTowardTheRightOfTheImage_IsBlockedAsTheWrongSide()
    {
        var verdict = CharacterIdentityAngleYawGate.Evaluate(
            CharacterIdentityAngleView.ThreeQuarterLeft, Measurement(noseOffset: 30.5), Deadband);

        Assert.False(verdict.Passed);
        Assert.NotNull(verdict.BlockReason);
        Assert.Contains("faces image-right", verdict.BlockReason!, StringComparison.Ordinal);
        Assert.Contains("needs the nose toward image-left", verdict.BlockReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_IsNeverAsserted_SoAMeasurementCannotPassOrBlockIt()
    {
        // A profile that happens to measure as facing the wrong way is still NOT the gate's business: a true
        // profile usually yields no face mesh at all, so direction stays the user's explicit confirmation (D4).
        var left = CharacterIdentityAngleYawGate.Evaluate(
            CharacterIdentityAngleView.ProfileLeft, Measurement(noseOffset: 48.6), Deadband);
        var right = CharacterIdentityAngleYawGate.Evaluate(
            CharacterIdentityAngleView.ProfileRight, Measurement(noseOffset: -48.6), Deadband);

        Assert.True(left.Passed);
        Assert.False(left.Asserted);
        Assert.True(right.Passed);
        Assert.False(right.Asserted);
        Assert.False(CharacterIdentityAngleYawGate.RequiresAssertion(CharacterIdentityAngleView.ProfileLeft));
        Assert.False(CharacterIdentityAngleYawGate.RequiresAssertion(CharacterIdentityAngleView.ProfileRight));
    }

    [Fact]
    public void ProfileWithoutAMeasurementAtAll_StillPassesAsUnasserted()
    {
        var verdict = CharacterIdentityAngleYawGate.Evaluate(
            CharacterIdentityAngleView.ProfileRight, measurement: null, Deadband);

        Assert.True(verdict.Passed);
        Assert.False(verdict.Asserted);
        Assert.Null(verdict.NoseOffsetPercent);
    }

    [Fact]
    public void GateIgnoresIrisAndInterocularValues_EvenWhenTheyAreNonsense()
    {
        // Regression guard: the accepted Becky angle set measured 3/4 left at −45.12 % nose offset while iris dy
        // read −13.63 % on the same image. If this gate ever consulted iris (or interocular) it would block a
        // correctly-turned head; here those numbers are absurd on purpose and the verdict must not move.
        var measurement = new CharacterIdentityEyeMeasurement
        {
            NoseOffsetPercent = -45.12,
            IrisDyPercent = 500.0,
            EyeDyPercent = -500.0,
            InterocularPixels = 0.0001
        };

        var verdict = CharacterIdentityAngleYawGate.Evaluate(
            CharacterIdentityAngleView.ThreeQuarterLeft, measurement, Deadband);

        Assert.True(verdict.Passed);
        Assert.True(verdict.Asserted);
    }

    [Fact]
    public void AssertedViewWithNoFaceMesh_IsBlockedAndNamesTheManualOverride()
    {
        var verdict = CharacterIdentityAngleYawGate.Evaluate(
            CharacterIdentityAngleView.ThreeQuarterRight,
            new CharacterIdentityEyeMeasurement { Error = "no face mesh" },
            Deadband);

        Assert.False(verdict.Passed);
        Assert.True(verdict.Asserted);
        Assert.Null(verdict.NoseOffsetPercent);
        Assert.Contains("no face", verdict.BlockReason!, StringComparison.Ordinal);
        Assert.Contains("manual", verdict.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssertedViewWithNoMeasurement_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CharacterIdentityAngleYawGate.Evaluate(
            CharacterIdentityAngleView.ThreeQuarterLeft, measurement: null, Deadband));

        Assert.Contains("cannot be evaluated without a measurement", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonPositiveDeadband_ThrowsRatherThanGuessingOne()
    {
        foreach (var deadband in new[] { 0.0, -5.0 })
        {
            var error = Assert.Throws<InvalidOperationException>(() => CharacterIdentityAngleYawGate.Evaluate(
                CharacterIdentityAngleView.ThreeQuarterLeft, Measurement(noseOffset: -45), deadband));

            Assert.Contains("AngleYawMinAbsPercent", error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(CharacterIdentityAngleView.ThreeQuarterLeft, -1)]
    [InlineData(CharacterIdentityAngleView.ProfileLeft, -1)]
    [InlineData(CharacterIdentityAngleView.ThreeQuarterRight, 1)]
    [InlineData(CharacterIdentityAngleView.ProfileRight, 1)]
    public void RequiredSign_IsMinusOneForLeftViews_PlusOneForRightViews(
        CharacterIdentityAngleView view, double expected)
        => Assert.Equal(expected, CharacterIdentityAngleYawGate.RequiredSign(view));

    private static CharacterIdentityEyeMeasurement Measurement(double noseOffset)
        => new() { NoseOffsetPercent = noseOffset };
}
