using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 B135-016 / D11 — the pinned compiler LLM declaration.
///
/// <para>
/// The load-bearing test here is <see cref="ASeedIsCarriedButItsAbsenceIsNotInvented"/> and its neighbours: the
/// completion path has NO seed parameter, so a declaration that pins one is carried and then refused by the compiler.
/// The failure this guards against is a run that RECORDS a seed it never sent — it would look reproducible and would
/// not be, which is the exact class of false evidence this workstream exists to remove.
/// </para>
/// </summary>
public sealed class ImageCellCompilerLlmSettingsTests
{
    [Fact]
    public void ParsesAModelAndTemperature()
    {
        var settings = ImageCellCompilerLlmSettings.Parse("""{"model":"qwen3.5-vl","temperature":0}""");

        Assert.Equal("qwen3.5-vl", settings.ModelIdentifier);
        Assert.Equal(0, settings.Temperature);
        Assert.Null(settings.Seed);
    }

    [Fact]
    public void ParsesADeclaredSeed()
    {
        var settings = ImageCellCompilerLlmSettings.Parse("""{"model":"qwen3.5-vl","temperature":0.2,"seed":42}""");

        Assert.Equal(0.2, settings.Temperature);
        Assert.Equal(42, settings.Seed);
    }

    [Fact]
    public void RoundTripsThroughSerialize()
    {
        // What a run records must be exactly what it declared, or the record is a paraphrase of the truth.
        var withSeed = new ImageCellCompilerLlmSettings("qwen3.5-vl", 0.25, 42);
        var withoutSeed = new ImageCellCompilerLlmSettings("qwen3.5-vl", 0);

        Assert.Equal(withSeed, ImageCellCompilerLlmSettings.Parse(withSeed.Serialize()));
        Assert.Equal(withoutSeed, ImageCellCompilerLlmSettings.Parse(withoutSeed.Serialize()));

        // The absent seed is ABSENT, not a zero that could be mistaken for a chosen value.
        Assert.DoesNotContain("seed", withoutSeed.Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void ATemperatureOfZeroIsAHonouredValueNotAMissingOne()
    {
        // 0 is the whole point of a pinned compiler (deterministic drafting), so a parser that treated 0 as "absent"
        // would refuse every correctly-pinned run.
        var settings = ImageCellCompilerLlmSettings.Parse("""{"model":"qwen3.5-vl","temperature":0}""");

        Assert.Equal(0, settings.Temperature);
        Assert.Contains("temperature\":0", settings.Serialize(), StringComparison.Ordinal);
    }

    // ---- refusals -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesAnEmptyDeclaration(string? json)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ImageCellCompilerLlmSettings.Parse(json));

        Assert.Contains("unpinned compiler", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesANonObjectDeclaration()
    {
        Assert.Throws<InvalidOperationException>(() => ImageCellCompilerLlmSettings.Parse("""["qwen3.5-vl"]"""));
        Assert.Throws<InvalidOperationException>(() => ImageCellCompilerLlmSettings.Parse("{not json"));
    }

    [Fact]
    public void RefusesADeclarationWithNoModel()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ImageCellCompilerLlmSettings.Parse("""{"temperature":0}"""));

        Assert.Contains("names no model", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesADeclarationWithNoTemperature()
    {
        // Temperature is a declared run variable, never a default: a prompt compiled at a temperature nobody chose is
        // not comparable to one compiled at the pinned value.
        var error = Assert.Throws<InvalidOperationException>(
            () => ImageCellCompilerLlmSettings.Parse("""{"model":"qwen3.5-vl"}"""));

        Assert.Contains("no temperature", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.5)]
    public void RefusesATemperatureOutsideTheProviderRange(double temperature)
    {
        var json = $$"""{"model":"qwen3.5-vl","temperature":{{temperature}}}""";

        var error = Assert.Throws<InvalidOperationException>(() => ImageCellCompilerLlmSettings.Parse(json));

        Assert.Contains("0-2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesASeedThatIsNotAWholeNumber()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ImageCellCompilerLlmSettings.Parse("""{"model":"qwen3.5-vl","temperature":0,"seed":"abc"}"""));

        Assert.Contains("whole number", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
