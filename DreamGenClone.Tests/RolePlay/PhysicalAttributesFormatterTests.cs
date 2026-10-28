using DreamGenClone.Domain.Templates;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public class PhysicalAttributesFormatterTests
{
    private static PhysicalAttributes AttrsWithRating(int? rating)
    {
        return new PhysicalAttributes
        {
            Age = "30",
            Height = "6'1\"",
            AttractivenessRating = rating
        };
    }

    // ── Renders tier prose for a set rating ────────────────────────────────

    [Fact]
    public void FormatBlock_Rating10_RendersStrikingProse()
    {
        var output = PhysicalAttributesFormatter.FormatBlock(AttrsWithRating(10), "Male");
        Assert.Contains("Attractiveness: 10/10 — Striking:", output);
        var striking = AttractivenessTierCatalog.Resolve(10)!;
        Assert.Contains(striking.Prose, output);
    }

    [Fact]
    public void FormatBlock_Rating9_RendersSameStrikingBand()
    {
        var output = PhysicalAttributesFormatter.FormatBlock(AttrsWithRating(9), "Male");
        Assert.Contains("Attractiveness: 9/10 — Striking:", output);
    }

    [Fact]
    public void FormatBlock_Rating5_RendersAverageProse()
    {
        var output = PhysicalAttributesFormatter.FormatBlock(AttrsWithRating(5), "Female");
        Assert.Contains("Attractiveness: 5/10 — Average:", output);
        var average = AttractivenessTierCatalog.Resolve(5)!;
        Assert.Contains(average.Prose, output);
    }

    [Fact]
    public void FormatBlock_Rating1_RendersRepellingProse()
    {
        var output = PhysicalAttributesFormatter.FormatBlock(AttrsWithRating(1), null);
        Assert.Contains("Attractiveness: 1/10 — Repelling:", output);
    }

    // ── Omits the line when null or out-of-range ───────────────────────────

    [Fact]
    public void FormatBlock_RatingNull_OmitsAttractivenessLine()
    {
        var output = PhysicalAttributesFormatter.FormatBlock(AttrsWithRating(null), "Male");
        Assert.DoesNotContain("Attractiveness", output);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-5)]
    [InlineData(100)]
    public void FormatBlock_RatingOutOfRange_OmitsAttractivenessLine(int rating)
    {
        var output = PhysicalAttributesFormatter.FormatBlock(AttrsWithRating(rating), "Male");
        Assert.DoesNotContain("Attractiveness", output);
    }

    // ── Gender-neutral (same prose for any gender) ─────────────────────────

    [Fact]
    public void FormatBlock_Rating10_ProseIsIdenticalAcrossGenders()
    {
        var male = PhysicalAttributesFormatter.FormatBlock(AttrsWithRating(10), "Male");
        var female = PhysicalAttributesFormatter.FormatBlock(AttrsWithRating(10), "Female");
        var unknown = PhysicalAttributesFormatter.FormatBlock(AttrsWithRating(10), "Unknown");

        var striking = AttractivenessTierCatalog.Resolve(10)!.Prose;
        Assert.Contains(striking, male);
        Assert.Contains(striking, female);
        Assert.Contains(striking, unknown);
    }

    [Fact]
    public void FormatVisualBlock_LabelsEyeColourAsIrisColor()
    {
        var output = PhysicalAttributesFormatter.FormatVisualBlock(new PhysicalAttributes { EyeColour = "blue" });

        Assert.Contains("Iris color: blue", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Eyes: blue", output, StringComparison.Ordinal);
    }

    // ── Figure line (B-103 body proportions) ──────────────────────────────

    [Fact]
    public void FormatVisualBlock_RendersFigureLine_AsProseScaleTerms()
    {
        var output = PhysicalAttributesFormatter.FormatVisualBlock(new PhysicalAttributes
        {
            BustSize = "Medium",
            WaistSize = "Soft",
            HipSize = "Wide",
            ButtSize = "Plump"
        });

        Assert.Contains("Figure: bust Medium, waist Soft, hips Wide, rear Plump", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatVisualBlock_OmitsFigureLine_WhenNoProportionsSet()
    {
        var output = PhysicalAttributesFormatter.FormatVisualBlock(new PhysicalAttributes { EyeColour = "blue" });

        Assert.DoesNotContain("Figure:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatVisualBlock_OmitsUnsetFigureParts()
    {
        var output = PhysicalAttributesFormatter.FormatVisualBlock(new PhysicalAttributes { ButtSize = "Plump" });

        Assert.Contains("Figure: rear Plump", output, StringComparison.Ordinal);
        Assert.DoesNotContain("bust", output, StringComparison.Ordinal);
        Assert.DoesNotContain("waist", output, StringComparison.Ordinal);
        Assert.DoesNotContain("hips", output, StringComparison.Ordinal);
    }

    // ── Body-only block (Composition Composer Body reference row) ────────────

    [Fact]
    public void FormatVisualBody_IncludesThePhysiqueAxesAndFigure_ButNoFaceAnchors()
    {
        var output = PhysicalAttributesFormatter.FormatVisualBody(new PhysicalAttributes
        {
            BodyBuild = "average frame",
            Adiposity = "average weight",
            BustSize = "Medium",
            HipSize = "Wide",
            HairColour = "brown",
            EyeColour = "blue"
        });

        Assert.Contains("Physique: average frame, average weight", output, StringComparison.Ordinal);
        Assert.Contains("Figure: bust Medium, hips Wide", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Hair", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Iris", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatVisualBody_ReturnsEmpty_WhenNoBodyFieldsSet()
    {
        var output = PhysicalAttributesFormatter.FormatVisualBody(new PhysicalAttributes { EyeColour = "blue" });

        Assert.Equal(string.Empty, output);
    }

    // ── Natural-language visual identity (APPEARANCE — AUTHORITATIVE block) ──

    [Fact]
    public void FormatVisualProse_RendersAgeBandAndJoinedTraits()
    {
        var output = PhysicalAttributesFormatter.FormatVisualProse(new PhysicalAttributes
        {
            Age = "51",
            Height = "5'8\"",
            Ethnicity = "Caucasian",
            HairStyle = "Short",
            HairColour = "Brown",
            EyeColour = "Blue",
            SkinTone = "Fair",
            SkinTexture = "Average",
            Adiposity = "average weight"
        });

        Assert.Contains("middle-aged", output, StringComparison.Ordinal);
        Assert.Contains("average height", output, StringComparison.Ordinal);
        Assert.Contains("caucasian", output, StringComparison.Ordinal);
        Assert.Contains("short brown hair", output, StringComparison.Ordinal);
        Assert.Contains("blue eyes", output, StringComparison.Ordinal);
        Assert.Contains("fair skin", output, StringComparison.Ordinal);
        Assert.Contains("average weight", output, StringComparison.Ordinal);

        Assert.DoesNotContain("Age", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Iris color", output, StringComparison.Ordinal);
        Assert.DoesNotContain("5'8", output, StringComparison.Ordinal);
        Assert.DoesNotContain("51", output, StringComparison.Ordinal);
        Assert.DoesNotContain("fifties", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatVisualProse_HairBun_ReadsAsHairInABun()
    {
        var output = PhysicalAttributesFormatter.FormatVisualProse(new PhysicalAttributes
        {
            HairStyle = "Bun",
            HairColour = "Brown"
        });

        Assert.Contains("brown hair in a bun", output, StringComparison.Ordinal);
        Assert.DoesNotContain("bun brown", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatVisualProse_HeightTall_SixFootOne()
    {
        var output = PhysicalAttributesFormatter.FormatVisualProse(new PhysicalAttributes { Height = "6'1\"" });

        Assert.Contains("tall", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatVisualProse_ReturnsEmpty_WhenNoVisualFieldsSet()
    {
        var output = PhysicalAttributesFormatter.FormatVisualProse(new PhysicalAttributes());

        Assert.Equal(string.Empty, output);
    }

    [Theory]
    [InlineData("21", "young")]
    [InlineData("39", "young")]
    [InlineData("40", "middle-aged")]
    [InlineData("51", "middle-aged")]
    [InlineData("60", "older")]
    [InlineData("72", "older")]
    public void FormatVisualProse_AgeBands_UseCategoriesNotNumbers(string age, string expected)
    {
        var output = PhysicalAttributesFormatter.FormatVisualProse(new PhysicalAttributes { Age = age });

        Assert.Contains(expected, output, StringComparison.Ordinal);
        Assert.DoesNotContain(age, output, StringComparison.Ordinal);
    }
}
