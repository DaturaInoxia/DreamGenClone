using System.Text.Json.Nodes;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Reading a pack's declaration block. The precedence rules are the whole point: a pack-wide rating, a category
/// default and a single file are three statements of different strength, and the most specific one has to win —
/// otherwise the per-pose escape hatch that makes a mixed category expressible would be silently ignored.
/// </summary>
public sealed class PosePackDeclarationsTests
{
    [Fact]
    public void ThePackRatingIsTheDefaultForEveryPose()
    {
        var declarations = Parse("""{ "rating": "nsfw" }""");

        Assert.Equal(PoseContentRating.Nsfw, declarations.For("standing", "standing/standing_01.json").Rating);
        Assert.Equal(PoseStance.Unknown, declarations.For("standing", "standing/standing_01.json").Stance);
    }

    [Fact]
    public void ACategoryKeyMayBeTheCategoryNameOrTheFolderPath()
    {
        var declarations = Parse(
            """
            {
              "rating": "nsfw",
              "categories": {
                "standing": { "stance": "standing" },
                "NSFW_lying": { "stance": "lying", "direction": "front" }
              }
            }
            """);

        Assert.Equal(
            PoseStance.Standing,
            declarations.For("standing", "standing/512768/standing028.json").Stance);

        // The path key is the only way to tell openpose-nsfw's two same-category folders apart, so it has to resolve
        // for a file several levels deep.
        Assert.Equal(
            PoseStance.Lying,
            declarations.For("lying", "NSFW_lying/512768/NSFW_lying017.json").Stance);
    }

    [Fact]
    public void AMoreSpecificEntryOverridesAMoreGeneralOne()
    {
        var declarations = Parse(
            """
            {
              "rating": "sfw",
              "categories": {
                "standing": { "stance": "standing", "direction": "front", "camera": "eye-level" },
                "standing/wide": { "direction": "three-quarter-left" }
              },
              "poses": {
                "standing/wide/w01.json": { "direction": "back", "rating": "nsfw" }
              }
            }
            """);

        var categoryOnly = declarations.For("standing", "standing/narrow/n01.json");
        Assert.Equal(PoseStance.Standing, categoryOnly.Stance);
        Assert.Equal(PoseFacingDirection.Front, categoryOnly.Direction);
        Assert.Equal(PoseContentRating.Sfw, categoryOnly.Rating);

        // The folder states one field; everything it does not state comes from the category.
        var folder = declarations.For("standing", "standing/wide/w02.json");
        Assert.Equal(PoseFacingDirection.ThreeQuarterLeft, folder.Direction);
        Assert.Equal(PoseStance.Standing, folder.Stance);
        Assert.Equal(PoseCameraAngle.EyeLevel, folder.Camera);

        // The file states two fields and keeps the rest.
        var file = declarations.For("standing", "standing/wide/w01.json");
        Assert.Equal(PoseFacingDirection.Back, file.Direction);
        Assert.Equal(PoseContentRating.Nsfw, file.Rating);
        Assert.Equal(PoseStance.Standing, file.Stance);
    }

    [Fact]
    public void CaseSpacesAndHyphensInAValueAreAllIgnored()
    {
        var declarations = Parse(
            """
            { "rating": "NSFW", "categories": { "s": { "stance": "All Fours", "direction": "Three-Quarter Left", "camera": "From Above" } } }
            """);

        var pose = declarations.For("s", "s/one.json");

        Assert.Equal(PoseStance.AllFours, pose.Stance);
        Assert.Equal(PoseFacingDirection.ThreeQuarterLeft, pose.Direction);
        Assert.Equal(PoseCameraAngle.FromAbove, pose.Camera);
        Assert.Equal(PoseContentRating.Nsfw, pose.Rating);
    }

    [Fact]
    public void AnUnrecognisedValueFailsAndNamesThePackTheKeyAndTheText()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Parse(
            """{ "categories": { "standing": { "stance": "stnading" } } }"""));

        Assert.Contains("categories.standing.stance", error.Message, StringComparison.Ordinal);
        Assert.Contains("stnading", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclarationThatIsNotAnObjectFailsWithTheShapeItWants()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Parse("""{ "categories": [ "standing" ] }"""));

        Assert.Contains("is not an object", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APackWithNoDeclarationBlockIsEmptyRatherThanAnError()
    {
        var declarations = Parse("""{ "name": "Some pack" }""");

        Assert.True(declarations.IsEmpty);

        var pose = declarations.For("standing", "standing/standing_01.json");
        Assert.True(pose.IsEmpty);
        Assert.Equal(PoseContentRating.Unrated, pose.Rating);
    }

    private static PosePackDeclarations Parse(string json) =>
        PosePackDeclarations.Parse((JsonObject)JsonNode.Parse(json)!, "test-pack");
}
