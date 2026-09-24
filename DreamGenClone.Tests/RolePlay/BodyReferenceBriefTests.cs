using DreamGenClone.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The frozen body-reference brief (B-122). Two invariants are pinned here.
///
/// First, the brief is the record of what was asked for: it carries the body-card version it was frozen from and
/// the authored body line, so a candidate image can always be traced back to the operator decision behind it.
///
/// Second, nothing defaults. A body reference missing its age, or with identity conditioning requested but no
/// reference named, fails fast — because such an image is indistinguishable from a correct one in the candidate
/// list, which would corrupt exactly the side-by-side comparison the deck exists for.
/// </summary>
public sealed class BodyReferenceBriefTests
{
    private static BodyReferenceBrief CompleteBrief() => new()
    {
        CharacterTemplateId = "becky-template",
        BodyCardVersion = 4,
        Gender = "Female",
        BodyState = SceneImageReferenceBodyState.Clothed,
        Age = "50",
        HairStyle = "bun",
        HairColour = "brown",
        EyeColour = "blue",
        SkinTone = "fair",
        SkinTexture = "smooth",
        BodyShape = "average frame, average weight, fuller rear with a soft belly, bust Full, hips Wide",
        Axes = new CharacterBodyAxes
        {
            BodyBuild = "average frame",
            Adiposity = "average weight",
            FatDistribution = "fuller rear with a soft belly",
            BustSize = "Full",
            HipSize = "Wide"
        },
        Stance = BodyReferenceStance.Standing,
        Clothing = "plain everyday clothing"
    };

    [Fact]
    public void ACompleteBrief_Validates()
    {
        var brief = CompleteBrief();

        brief.Validate();
    }

    [Fact]
    public void TheBrief_CarriesTheCardVersionAndLine_SoACandidateTracesToItsDecision()
    {
        var brief = CompleteBrief();

        Assert.Equal(4, brief.BodyCardVersion);
        Assert.Contains("fuller rear with a soft belly", brief.BodyShape, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both target families need an explicit age — Pony falls back to its own attractive-adult prior without one
    /// (validated rule 12), and the SDXL anatomy leads with subject/appearance. So the age is required, not optional.
    /// </summary>
    [Fact]
    public void AMissingAge_FailsFastNamingIt()
    {
        var brief = CompleteBrief();
        brief.Age = "   ";

        var error = Assert.Throws<InvalidOperationException>(brief.Validate);

        Assert.Contains("the age", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingBodyShapeLine_FailsFast_AndSaysTheCardMustBeComplete()
    {
        var brief = CompleteBrief();
        brief.BodyShape = string.Empty;

        var error = Assert.Throws<InvalidOperationException>(brief.Validate);

        Assert.Contains("body card must be complete", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingCharacterTemplate_FailsFast()
    {
        var brief = CompleteBrief();
        brief.CharacterTemplateId = "  ";

        var error = Assert.Throws<InvalidOperationException>(brief.Validate);

        Assert.Contains("character template id", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Identity is two facts that must agree. Requesting it with nothing to condition on would silently render an
    /// unconditioned image that looks like every other candidate.
    /// </summary>
    [Fact]
    public void RequestingIdentity_WithoutAFaceReference_FailsFast()
    {
        var brief = CompleteBrief();
        brief.RequiresIdentity = true;
        brief.IdentityFaceAssetId = null;

        var error = Assert.Throws<InvalidOperationException>(brief.Validate);

        Assert.Contains("nothing to condition on", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The other half of the same contract: a named reference that was not requested would be ignored.</summary>
    [Fact]
    public void NamingAFaceReference_WithoutRequestingIdentity_FailsFast()
    {
        var brief = CompleteBrief();
        brief.RequiresIdentity = false;
        brief.IdentityFaceAssetId = "face-asset-1";

        var error = Assert.Throws<InvalidOperationException>(brief.Validate);

        Assert.Contains("would be ignored", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestingIdentity_WithAFaceReference_Validates()
    {
        var brief = CompleteBrief();
        brief.RequiresIdentity = true;
        brief.IdentityFaceAssetId = "face-asset-1";
        brief.IdentityPackId = "pack-1";

        brief.Validate();
    }

    /// <summary>
    /// A face asset with no pack could not be re-verified before the render, so the render could condition on a
    /// reference that was superseded in between. Both are required together.
    /// </summary>
    [Fact]
    public void RequestingIdentity_WithoutNamingThePack_FailsFast()
    {
        var brief = CompleteBrief();
        brief.RequiresIdentity = true;
        brief.IdentityFaceAssetId = "face-asset-1";
        brief.IdentityPackId = null;

        var error = Assert.Throws<InvalidOperationException>(brief.Validate);

        Assert.Contains("no approved pack", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NamingAPack_WithoutRequestingIdentity_FailsFast()
    {
        var brief = CompleteBrief();
        brief.RequiresIdentity = false;
        brief.IdentityPackId = "pack-1";

        var error = Assert.Throws<InvalidOperationException>(brief.Validate);

        Assert.Contains("would be ignored", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only the OpenPoseXL2-verified stances are offered, and an unverified value is refused rather than rendered:
    /// a reference whose pose drifts is not comparable to the next one, which is the point of generating several.
    /// </summary>
    [Fact]
    public void OnlyTheVerifiedStances_AreOffered_AndAnUnverifiedOneIsRefused()
    {
        Assert.Equal(
            new[] { BodyReferenceStance.Standing, BodyReferenceStance.Squatting, BodyReferenceStance.Kneeling },
            Enum.GetValues<BodyReferenceStance>());

        var brief = CompleteBrief();
        brief.Stance = (BodyReferenceStance)99;

        var error = Assert.Throws<InvalidOperationException>(brief.Validate);

        Assert.Contains("Unsupported body reference stance", error.Message, StringComparison.Ordinal);
        Assert.Contains("Standing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStanceIsAlwaysDeclared_AndDefaultsToStanding()
    {
        Assert.Equal(BodyReferenceStance.Standing, new BodyReferenceBrief().Stance);
    }

    /// <summary>
    /// Gender is a stated fact, not an inference. The compiler used to derive the noun from whether a bust
    /// measurement was present, so a flat-chested woman was rendered as a man — a guess wearing the costume of a
    /// mapping. "Unknown" is the template's own unset value and is refused for the same reason a blank is.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown")]
    public void AnUnstatedGender_FailsFast(string gender)
    {
        var brief = CompleteBrief();
        brief.Gender = gender;

        var error = Assert.Throws<InvalidOperationException>(brief.Validate);

        Assert.Contains("the gender", error.Message, StringComparison.Ordinal);
        Assert.Contains("Male or Female", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Male")]
    [InlineData("Female")]
    [InlineData("female")]
    public void AStatedGender_Validates(string gender)
    {
        var brief = CompleteBrief();
        brief.Gender = gender;

        brief.Validate();
    }

    /// <summary>
    /// Clothing and state must agree BOTH ways. A clothed reference with no outfit leaves the model to invent one;
    /// an unclothed reference that still names an outfit contradicts itself. Neither is quietly normalised, because
    /// a reference sheet is only useful if it means one thing.
    /// </summary>
    [Fact]
    public void AClothedReference_WithoutClothing_FailsFast()
    {
        var brief = CompleteBrief();
        brief.BodyState = SceneImageReferenceBodyState.Clothed;
        brief.Clothing = "  ";

        var error = Assert.Throws<InvalidOperationException>(brief.Validate);

        Assert.Contains("would invent an outfit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnclothedReference_CarryingClothing_FailsFast()
    {
        var brief = CompleteBrief();
        brief.BodyState = SceneImageReferenceBodyState.Unclothed;
        brief.Clothing = "plain everyday clothing";

        var error = Assert.Throws<InvalidOperationException>(brief.Validate);

        Assert.Contains("contradicts the view", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnclothedReference_WithoutClothing_Validates()
    {
        var brief = CompleteBrief();
        brief.BodyState = SceneImageReferenceBodyState.Unclothed;
        brief.Clothing = null;

        brief.Validate();
    }

    [Fact]
    public void AnUndefinedBodyState_FailsFast()
    {
        var brief = CompleteBrief();
        brief.BodyState = (SceneImageReferenceBodyState)99;

        var error = Assert.Throws<InvalidOperationException>(brief.Validate);

        Assert.Contains("not a body state", error.Message, StringComparison.Ordinal);
    }
}
