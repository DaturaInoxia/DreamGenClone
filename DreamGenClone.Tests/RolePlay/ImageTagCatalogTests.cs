using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The image-tag vocabulary (B-140 D2): one owner for the spelling, so a tag written by a render and a tag typed into
/// a search box are the same string.
///
/// <para>
/// What is pinned here is the part a later change could quietly break: the normalization that makes "3/4 left" and
/// "3-4-left" one tag, the rule that an UNDECLARED pose value produces NO tag (because <c>stance:unknown</c> would be a
/// search hit for a fact nobody stated), and the refusal of a tag shape nothing could find.
/// </para>
/// </summary>
public sealed class ImageTagCatalogTests
{
    [Theory]
    [InlineData("3/4 left", "3-4-left")]
    [InlineData("all fours", "all-fours")]
    [InlineData("T-pose", "t-pose")]
    [InlineData("  Becky  ", "becky")]
    [InlineData("Krea2 NSFW unlock", "krea2-nsfw-unlock")]
    [InlineData("krea2_nsfw_v4_v43exp.safetensors", "krea2-nsfw-v4-v43exp-safetensors")]
    public void Normalize_MakesOneSpellingOutOfTheWordsAnOperatorWouldType(string raw, string expected)
        => Assert.Equal(expected, ImageTagCatalog.Normalize(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("---")]
    [InlineData("///")]
    public void Normalize_OfSomethingWithNoWords_IsEmptySoNoTagIsWritten(string? raw)
        => Assert.Equal(string.Empty, ImageTagCatalog.Normalize(raw));

    [Fact]
    public void Tag_ComposesThePrefixAndTheNormalizedValue()
        => Assert.Equal("stance:kneeling", ImageTagCatalog.Tag(ImageTagCatalog.Stance, "Kneeling"));

    [Fact]
    public void Tag_OfNothing_IsNullRatherThanAPlaceholder()
        => Assert.Null(ImageTagCatalog.Tag(ImageTagCatalog.Stance, "   "));

    [Fact]
    public void Tag_WithAPrefixTheCatalogDoesNotOwn_IsRefusedByName()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ImageTagCatalog.Tag("person", "becky"));

        // The message names the axis list, because "invent a new prefix" is the mistake this prevents and the fix is
        // to pick one that exists.
        Assert.Contains("stance", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TagHelpers_OmitEveryValueThatWasNeverDeclared()
    {
        // "Not declared" is not a tag: a search for stance:unknown would be a hit for a fact nobody stated.
        Assert.Null(ImageTagCatalog.StanceTag(PoseStance.Unknown));
        Assert.Null(ImageTagCatalog.DirectionTag(PoseFacingDirection.Unknown));
        Assert.Null(ImageTagCatalog.CameraTag(PoseCameraAngle.Unknown));
        Assert.Null(ImageTagCatalog.RatingTag(PoseContentRating.Unrated));
    }

    [Fact]
    public void TagHelpers_UseThePoseLibrariesOwnWords()
    {
        // The same words the pose card shows, so an image tag and a pose keyword cannot disagree about "all fours".
        Assert.Equal("stance:all-fours", ImageTagCatalog.StanceTag(PoseStance.AllFours));
        Assert.Equal("direction:3-4-left", ImageTagCatalog.DirectionTag(PoseFacingDirection.ThreeQuarterLeft));
        Assert.Equal("camera:from-above", ImageTagCatalog.CameraTag(PoseCameraAngle.FromAbove));
        Assert.Equal("rating:nsfw", ImageTagCatalog.RatingTag(PoseContentRating.Nsfw));
    }

    [Fact]
    public void StanceFromName_LandsOnTheSameTagAPresetProduces()
    {
        // A payload stores the enum NAME. Slugging it directly would write stance:allfours and a search for the words
        // an operator sees ("all fours") would miss half the library.
        Assert.Equal("stance:all-fours", ImageTagCatalog.StanceFromName("AllFours"));
        Assert.Equal("stance:kneeling", ImageTagCatalog.StanceFromName("kneeling"));
        Assert.Null(ImageTagCatalog.StanceFromName("Unknown"));
        Assert.Null(ImageTagCatalog.StanceFromName("5"));
        Assert.Null(ImageTagCatalog.StanceFromName("not a stance"));
    }

    [Fact]
    public void FromPosePreset_TagsTheNameCategoryLibraryAndTheFourAxes()
    {
        var preset = new PosePreset
        {
            Id = "preset-1",
            Name = "All Fours 2.1",
            Category = "all fours",
            LibraryId = "library-1",
            Stance = PoseStance.AllFours,
            Direction = PoseFacingDirection.ThreeQuarterLeft,
            CameraAngle = PoseCameraAngle.FromAbove,
            ContentRating = PoseContentRating.Nsfw
        };

        var tags = ImageTagCatalog.FromPosePreset(preset, "Imported packs").ToList();

        Assert.Equal(
            [
                "pose:all-fours-2-1",
                "category:all-fours",
                "library:imported-packs",
                "stance:all-fours",
                "direction:3-4-left",
                "camera:from-above",
                "rating:nsfw"
            ],
            tags);
    }

    [Fact]
    public void FromPosePreset_OfAPackThatDeclaresNothing_TagsOnlyWhatIsDeclared()
    {
        var preset = new PosePreset { Id = "preset-2", Name = "Untitled", Category = string.Empty };

        var tags = ImageTagCatalog.FromPosePreset(preset).ToList();

        // The name survives; nothing else is invented.
        Assert.Equal(["pose:untitled"], tags);
    }

    [Fact]
    public void NormalizeAll_KeepsTheOrderAndDropsDuplicates()
    {
        var tags = ImageTagCatalog.NormalizeAll(
            ["stance:kneeling", " Stance:Kneeling ", "rating:nsfw", null, "  ", "character:Becky"]);

        Assert.Equal(["stance:kneeling", "rating:nsfw", "character:becky"], tags);
    }

    [Fact]
    public void NormalizeAll_RefusesATagWithNoPrefix()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ImageTagCatalog.NormalizeAll(["kneeling"]));

        Assert.Contains("prefix:value", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("stance:kneeling", true)]
    [InlineData("STANCE:kneeling", false)] // The persisted form is lower-case; an upper-case prefix is not one of ours.
    [InlineData("person:becky", false)]
    [InlineData("stance:", false)]
    [InlineData("kneeling", false)]
    [InlineData("", false)]
    public void IsCatalogTag_AcceptsOnlyThisVocabularysShape(string tag, bool expected)
        => Assert.Equal(expected, ImageTagCatalog.IsCatalogTag(tag));

    [Fact]
    public void Parse_OfARowEditedOutsideTheApp_SkipsTheUnreadableTagsRatherThanThrowing()
    {
        // A read runs over a LIST of images: one bad row must not take out the list.
        var tags = ImageTagCatalog.Parse("""["stance:kneeling","person:becky","","rating:NSFW"]""");

        Assert.Equal(["stance:kneeling", "rating:nsfw"], tags);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("{\"tags\":[]}")]
    public void Parse_OfNothingReadable_IsNoTags(string? json)
        => Assert.Empty(ImageTagCatalog.Parse(json));

    [Fact]
    public void Serialize_StoresTheNormalizedListSoNothingUnsearchableIsWritten()
        => Assert.Equal("""["stance:kneeling"]""", ImageTagCatalog.Serialize([" Stance:Kneeling "]));

    [Fact]
    public void Describe_ShowsTheValueInWordsWithoutTheAxis()
        => Assert.Equal("all fours", ImageTagCatalog.Describe("stance:all-fours"));
}
