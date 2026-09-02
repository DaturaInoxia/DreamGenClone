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
}
