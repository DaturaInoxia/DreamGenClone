using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The wardrobe-item prompt compiler is what turns "a yellow sundress" into the long, explicit garment reference the
/// render uses verbatim, so its rules are load-bearing: the framing and backdrop it states are the mechanism by which
/// the garment later transfers into other images, and a compiler that served the wrong dialect would write a garment
/// prompt its model cannot read.
/// </summary>
public sealed class WardrobeItemPromptCompilerTests
{
    private static WardrobeItemPromptCompilerRegistry Registry() => new(
    [
        new NaturalLanguageWardrobeItemPromptCompiler(SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage),
        new NaturalLanguageWardrobeItemPromptCompiler(SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage),
        new NaturalLanguageWardrobeItemPromptCompiler(SceneImageModelFamily.Flux, SceneImagePromptDialect.FluxNaturalLanguage),
        new NaturalLanguageWardrobeItemPromptCompiler(SceneImageModelFamily.Api, SceneImagePromptDialect.NaturalLanguage)
    ]);

    [Fact]
    public void Require_QwenImage21_ResolvesTheNaturalLanguageCompiler()
    {
        var compiler = Registry().Require(SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);

        Assert.Equal(SceneImageModelFamily.QwenImage21, compiler.Family);
        Assert.Equal(SceneImagePromptDialect.NaturalLanguage, compiler.PromptDialect);
    }

    /// <summary>
    /// Pony is deliberately unregistered: its dialect is a tag list, and the garment rules are prose. Refusing by name
    /// is what stops a sundress coming back as `score_9, 1girl, dress`.
    /// </summary>
    [Fact]
    public void Require_PonyTags_FailsNamingTheRegistration()
    {
        var registry = Registry();

        Assert.False(registry.TryResolve(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags, out _));
        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.Require(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags));

        Assert.Contains("Pony", exception.Message, StringComparison.Ordinal);
        Assert.Contains("QwenImage21", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMessages_StatesTheGarmentAndTheReferenceFramingRules()
    {
        var compiler = new NaturalLanguageWardrobeItemPromptCompiler(
            SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);

        var (system, user) = compiler.BuildMessages(new WardrobeItemPromptRequest("a blue one piece swimsuit", "1024x1024"));

        // The operator's words reach the drafting model verbatim: the compiler expands them, it does not paraphrase them.
        Assert.Contains("a blue one piece swimsuit", user, StringComparison.Ordinal);
        Assert.Contains("1024x1024", user, StringComparison.Ordinal);

        // The frame's inventory, stated as contents rather than as exclusions.
        Assert.Contains("PRODUCT PHOTOGRAPH", system, StringComparison.Ordinal);
        Assert.Contains("laid flat", system, StringComparison.Ordinal);
        Assert.Contains("light-grey surface", system, StringComparison.Ordinal);
        Assert.Contains("e-commerce product photography", system, StringComparison.Ordinal);
        Assert.Contains("sharp focus", system, StringComparison.Ordinal);
        Assert.Contains("filling the frame", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// Operator correction 2026-09-29: "it is adding a person wearing the garment, the compiler should know not do to
    /// that", and then "best practices are to not mention things you do NOT want to see, mention only things you want to
    /// see". A person in the reference leaks their body, pose and clothes into every render that binds it, and a NAMED
    /// person - even a negated one - conditions a model that has no negative prompt.
    /// </summary>
    [Fact]
    public void BuildMessages_AsksForNoWearerAndForbidsNegationsInTheOutput()
    {
        var compiler = new NaturalLanguageWardrobeItemPromptCompiler(
            SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);

        var (system, user) = compiler.BuildMessages(new WardrobeItemPromptRequest("a long t-shirt worn as a night gown", "896x1152"));

        // No instruction to show a wearer can come back.
        Assert.DoesNotContain("worn by a person", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("the wearer", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("the face is not in the frame", system, StringComparison.Ordinal);

        // And the RULES are affirmative: state what is in the frame; never phrase an exclusion.
        Assert.Contains("WRITE AFFIRMATIVELY", system, StringComparison.Ordinal);
        Assert.Contains("THE PHOTOGRAPH CONTAINS THE GARMENT AND THE SURFACE IT LIES ON", system, StringComparison.Ordinal);
        Assert.Contains("delete the whole clause", system, StringComparison.Ordinal);
        Assert.DoesNotContain("the exact words", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no person, no hanger, no props", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no person", user, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The rule enforced, not just requested. The drafting model is told to write affirmatively, and a slip is refused
    /// here rather than shipped: a negated noun is still that noun to a model with no negative prompt. A word that
    /// merely CONTAINS a forbidden one (bodice, texture) is untouched.
    /// </summary>
    [Theory]
    [InlineData("A product photograph of a yellow cotton sundress laid flat on a plain light-grey surface, sleeveless with a scoop neckline and a full gathered skirt that ends below the knee, even diffused lighting from above, sharp focus, with no person in the frame, photorealistic e-commerce product photography, the entire garment centred.")]
    [InlineData("A product photograph of a yellow cotton sundress laid flat on a plain light-grey surface, sleeveless with a scoop neckline and a full gathered skirt that ends below the knee, even diffused lighting from above, sharp focus, shown without a mannequin, photorealistic e-commerce product photography, the entire garment centred and filling the frame.")]
    [InlineData("A product photograph of a blue one piece swimsuit worn by a woman, high scoop neckline and wide straps, laid flat on a plain light-grey surface, even diffused lighting from above, sharp focus, photorealistic e-commerce product photography, the entire swimsuit centred and filling the frame as it lies.")]
    [InlineData("A product photograph of a black satin dress laid flat on a plain light-grey surface beside a wooden hanger, straight with its front facing the camera, even diffused lighting from above, sharp focus, photorealistic e-commerce product photography, the entire dress centred and filling the frame as it lies.")]
    public void ParseOutput_NamesSomethingAbsent_Fails(string drafted)
    {
        var compiler = new NaturalLanguageWardrobeItemPromptCompiler(
            SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);

        var exception = Assert.Throws<InvalidOperationException>(() => compiler.ParseOutput(drafted));

        Assert.Contains("does not contain", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no negative prompt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_AffirmativeGarmentPrompt_Passes()
    {
        var compiler = new NaturalLanguageWardrobeItemPromptCompiler(
            SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);
        const string drafted = "A product photograph of a yellow cotton sundress laid flat on a plain light-grey surface, "
            + "sleeveless with a scoop neckline, thin shoulder straps and a fitted smocked bodice above a full gathered "
            + "skirt that ends below the knee, the bodice and skirt smoothed so the cut reads, even diffused lighting from "
            + "above with soft falloff, accurate cotton texture, sharp focus, photorealistic e-commerce product photography.";

        Assert.Equal(drafted, compiler.ParseOutput(drafted));
    }

    /// <summary>
    /// Real garment wording must survive the guard (2026-09-29): a gender word as a CATEGORY QUALIFIER is not a person
    /// in the frame, and "body" is fit vocabulary (a loose body, relaxed through the body) rather than a subject. A
    /// guard that refused these would reject the honest description of the very garments this feature exists for.
    /// </summary>
    [Theory]
    [InlineData("A product photograph of women's boxer shorts laid flat on a plain light-grey surface, cut very short with a five centimetre inseam, a loose boxy body that is relaxed through the body and hangs wide at the leg opening, curved hem swept up at the outer thigh, soft elasticated waistband, even diffused lighting from above, sharp focus, photorealistic e-commerce product photography.")]
    [InlineData("A product photograph of men's swim shorts laid flat on a plain light-grey surface, mid-length with a straight leg and a drawstring waistband, quick-dry fabric with a small side seam detail, even diffused lighting from above, sharp focus, photorealistic e-commerce product photography, the whole garment centred and filling the frame.")]
    public void ParseOutput_GarmentVocabulary_IsNotMistakenForASubject(string drafted)
    {
        var compiler = new NaturalLanguageWardrobeItemPromptCompiler(
            SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);

        Assert.Equal(drafted, compiler.ParseOutput(drafted));
    }

    [Fact]
    public void BuildMessages_BlankDescription_Fails()
    {
        var compiler = new NaturalLanguageWardrobeItemPromptCompiler(
            SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);

        var exception = Assert.Throws<InvalidOperationException>(
            () => compiler.BuildMessages(new WardrobeItemPromptRequest("   ", "1024x1024")));

        Assert.Contains("description", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseOutput_UnwrapsAFencedBlock()
    {
        var compiler = new NaturalLanguageWardrobeItemPromptCompiler(
            SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);
        var body = new string('x', NaturalLanguageWardrobeItemPromptCompiler.MinimumOutputChars + 20);

        var parsed = compiler.ParseOutput($"```\n{body}\n```");

        Assert.Equal(body, parsed);
    }

    [Fact]
    public void ParseOutput_UnwrapsAJsonEnvelope()
    {
        var compiler = new NaturalLanguageWardrobeItemPromptCompiler(
            SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);
        var body = new string('y', NaturalLanguageWardrobeItemPromptCompiler.MinimumOutputChars + 20);

        var parsed = compiler.ParseOutput($"{{\"prompt\":\"{body}\"}}");

        Assert.Equal(body, parsed);
    }

    /// <summary>
    /// A stored prompt is used VERBATIM by the render (the image row names its compiler), so a one-line answer must be
    /// refused here rather than shipped as the final prompt of a reference image.
    /// </summary>
    [Fact]
    public void ParseOutput_TooShort_Fails()
    {
        var compiler = new NaturalLanguageWardrobeItemPromptCompiler(
            SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);

        var exception = Assert.Throws<InvalidOperationException>(() => compiler.ParseOutput("a yellow sundress"));

        Assert.Contains("too short", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_Empty_Fails()
    {
        var compiler = new NaturalLanguageWardrobeItemPromptCompiler(
            SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);

        Assert.Throws<InvalidOperationException>(() => compiler.ParseOutput("   "));
    }

    [Fact]
    public void Constructor_RefusesATagDialect()
    {
        Assert.Throws<ArgumentException>(() => new NaturalLanguageWardrobeItemPromptCompiler(
            SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags));
    }
}
