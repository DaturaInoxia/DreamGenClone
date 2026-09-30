using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 B135-011 — the cell's binding declaration.
///
/// <para>
/// Every test here is about REFUSAL, and that is the point. A binding declaration is the only thing that makes a cell
/// reproducible; a declaration that is silently trimmed, ignored or mis-typed produces a render that looks like the
/// declared one and is not — which is worse than a failure, because it reads as evidence.
/// </para>
/// </summary>
public sealed class ImageCellBindingsTests
{
    [Fact]
    public void AFullDeclarationParsesWithEveryMechanism()
    {
        var bindings = ImageCellBindings.Parse("""
            [
              {"axis":"Identity","mode":"Lora","value":"becky-v7","strength":0.8},
              {"axis":"Pose","mode":"Adapter","strategy":"OpenPose"},
              {"axis":"Location","mode":"Reference","value":"asset-pine-clearing"},
              {"axis":"Wardrobe","mode":"Text"}
            ]
            """);

        Assert.Equal(4, bindings.Count);

        var identity = bindings.Single(binding => binding.Axis == ImageBindingAxis.Identity);
        Assert.Equal(ImageBindingMode.Lora, identity.Mode);
        Assert.Equal("becky-v7", identity.Value);
        Assert.Equal(0.8, identity.Strength);

        var pose = bindings.Single(binding => binding.Axis == ImageBindingAxis.Pose);
        Assert.Equal(ImageBindingMode.Adapter, pose.Mode);
        Assert.Equal("OpenPose", pose.Strategy);

        var wardrobe = bindings.Single(binding => binding.Axis == ImageBindingAxis.Wardrobe);
        Assert.Equal(ImageBindingMode.Text, wardrobe.Mode);
        Assert.Null(wardrobe.Value);
    }

    [Fact]
    public void AnEmptyDeclarationIsLegal()
    {
        // A cell may carry everything in the prompt; that is a declaration, not a missing one.
        Assert.Empty(ImageCellBindings.Parse(null));
        Assert.Empty(ImageCellBindings.Parse("  "));
        Assert.Empty(ImageCellBindings.Parse("[]"));
    }

    [Fact]
    public void ADeclarationRoundTrips()
    {
        const string json = """
            [{"axis":"Identity","mode":"Lora","value":"becky-v7","strength":0.8},{"axis":"Pov","mode":"Text"}]
            """;

        var parsed = ImageCellBindings.Parse(json);
        var reparsed = ImageCellBindings.Parse(ImageCellBindings.Serialize(parsed));

        Assert.Equal(parsed.Count, reparsed.Count);
        Assert.Equal(parsed[0], reparsed[0]);
        Assert.Equal(parsed[1], reparsed[1]);
    }

    [Fact]
    public void AxisAndModeNamesAreCaseInsensitive()
    {
        var bindings = ImageCellBindings.Parse("""[{"axis":"identity","mode":"lora","value":"becky-v7","strength":0.8}]""");

        Assert.Equal(ImageBindingAxis.Identity, bindings[0].Axis);
        Assert.Equal(ImageBindingMode.Lora, bindings[0].Mode);
    }

    // ---- refusals -----------------------------------------------------------------------------------------

    [Fact]
    public void AnUnknownAxisIsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ImageCellBindings.Parse("""[{"axis":"WardrobeColour","mode":"Text"}]"""));

        Assert.Contains("WardrobeColour", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownModeIsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ImageCellBindings.Parse("""[{"axis":"Pose","mode":"Skeleton"}]"""));

        Assert.Contains("Skeleton", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMisspelledPropertyIsRefusedRatherThanIgnored()
    {
        // 'strenght' would otherwise be dropped, and the LoRA would be applied at a strength nobody chose - which the
        // render path already refuses for the same reason.
        var error = Assert.Throws<InvalidOperationException>(
            () => ImageCellBindings.Parse("""[{"axis":"Identity","mode":"Lora","value":"becky-v7","strenght":0.8}]"""));

        Assert.Contains("strenght", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown property", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnAxisDeclaredTwiceIsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ImageCellBindings.Parse("""
            [{"axis":"Pose","mode":"Text"},{"axis":"Pose","mode":"Adapter","strategy":"OpenPose"}]
            """));

        Assert.Contains("twice", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ALoraWithoutAPositiveStrengthIsRefused()
    {
        var missing = Assert.Throws<InvalidOperationException>(
            () => ImageCellBindings.Parse("""[{"axis":"Identity","mode":"Lora","value":"becky-v7"}]"""));
        Assert.Contains("positive strength", missing.Message, StringComparison.OrdinalIgnoreCase);

        var zero = Assert.Throws<InvalidOperationException>(
            () => ImageCellBindings.Parse("""[{"axis":"Identity","mode":"Lora","value":"becky-v7","strength":0}]"""));
        Assert.Contains("positive strength", zero.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AReferenceWithNothingBoundIsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ImageCellBindings.Parse("""[{"axis":"Location","mode":"Reference"}]"""));

        Assert.Contains("names nothing", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ATextAxisCarryingAValueIsRefused()
    {
        // Text means the PROMPT carries the axis. A value alongside it would be a second source that nothing compares.
        var error = Assert.Throws<InvalidOperationException>(
            () => ImageCellBindings.Parse("""[{"axis":"Wardrobe","mode":"Text","value":"sundress"}]"""));

        Assert.Contains("second", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnAdapterWithoutAStrategyIsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ImageCellBindings.Parse("""[{"axis":"Pose","mode":"Adapter"}]"""));

        Assert.Contains("adapter strategy", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AReferenceMayPinItsMechanismAndStrength()
    {
        // Deliberate, and the reason is the comparison suite: "which mechanism carries this identity" and "how
        // strongly" are variables the playground exists to change, so a cell must be able to pin them.
        var bindings = ImageCellBindings.Parse(
            """[{"axis":"Identity","mode":"Reference","value":"pack-becky-v7","strategy":"ip-adapter","strength":0.7}]""");

        var only = Assert.Single(bindings);
        Assert.Equal("ip-adapter", only.Strategy);
        Assert.Equal(0.7, only.Strength);
    }

    [Fact]
    public void AReferenceAtANonPositiveStrengthIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => ImageCellBindings.Parse(
            """[{"axis":"Identity","mode":"Reference","value":"pack-becky-v7","strength":0}]"""));
    }

    [Fact]
    public void AMalformedDeclarationIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => ImageCellBindings.Parse("{not json"));
        Assert.Throws<InvalidOperationException>(() => ImageCellBindings.Parse("""{"axis":"Pose","mode":"Text"}"""));
        Assert.Throws<InvalidOperationException>(() => ImageCellBindings.Parse("""["Pose"]"""));
    }

    [Fact]
    public void ValidateAppliesTheSameRulesDirectly()
    {
        // The in-memory path and the JSON path share one definition, so a caller cannot construct an invalid
        // declaration through the typed API that the JSON path would have refused.
        var invalid = new List<ImageCellBinding> { new(ImageBindingAxis.Identity, ImageBindingMode.Lora, "becky-v7") };

        Assert.Throws<InvalidOperationException>(() => ImageCellBindings.Validate(invalid));
        Assert.Throws<InvalidOperationException>(() => ImageCellBindings.Serialize(invalid));
    }

    [Fact]
    public void TheShapeUsedByTheSuiteStoreParses()
    {
        // Cross-check against the declaration the suite-store tests persist, so the two cannot drift apart.
        var bindings = ImageCellBindings.Parse("""[{"axis":"Identity","mode":"Lora","value":"becky-v7","strength":0.8}]""");

        var only = Assert.Single(bindings);
        Assert.Equal(ImageBindingAxis.Identity, only.Axis);
        Assert.Equal(0.8, only.Strength);
    }
}
