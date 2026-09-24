using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The per-family body-reference prompt compiler (B-122).
///
/// These tests pin the two things that were wrong with the prompt the operator reported: the same flat string was
/// going to every family, and the values in it were vague or inert. Each family is asserted against its own
/// validated rules, and the reported prompt's own defects are asserted to be absent from compiled output.
/// </summary>
public sealed class BodyReferencePromptCompilerTests
{
    private static BodyReferenceBrief Brief() => new()
    {
        CharacterTemplateId = "becky-template",
        BodyCardVersion = 4,
        Gender = "Female",
        BodyState = SceneImageReferenceBodyState.Clothed,
        Age = "50",
        HairStyle = "Bun",
        HairColour = "Brown",
        EyeColour = "blue",
        SkinTone = "Fair",
        BodyShape = "average frame, average weight, fuller rear with a soft belly, bust Full, hips Wide",
        Axes = new CharacterBodyAxes
        {
            BodyBuild = "average frame",
            Adiposity = "a little soft, slight roundness",
            FatDistribution = "fuller rear with a soft belly",
            MuscleMass = "no visible muscle",
            MuscleDefinition = "soft, no definition",
            BustSize = "Full",
            HipSize = "Wide",
            ButtSize = "full"
        },
        Stance = BodyReferenceStance.Standing,
        Clothing = "plain everyday clothing"
    };

    private static CompiledBodyPrompt Pony(BodyReferenceBrief? brief = null)
        => BodyReferencePromptCompiler.Compile(
            brief ?? Brief(), BodyPromptFamily.Pony, "rating_safe", "1girl", ["Becky", "Dean"]);

    private static CompiledBodyPrompt Sdxl(BodyReferenceBrief? brief = null)
        => BodyReferencePromptCompiler.Compile(
            brief ?? Brief(), BodyPromptFamily.Sdxl, forbiddenTokens: ["Becky", "Dean"]);

    // ── Pony: the validated ordering and required components (rules 1, 2, 4, 5, 12) ────────────────────────

    [Fact]
    public void Pony_StartsWithTheFullQualityString_ThenRatingThenCount()
    {
        var prompt = Pony().Positive;
        var quality = PonySceneImagePromptBuilder.PonyQualityTags;

        // Rule 1: the FULL six-tag string, first. The short form is documented as much weaker.
        Assert.StartsWith(quality, prompt, StringComparison.Ordinal);
        // Rule 2 and rule 4 immediately after.
        Assert.Contains($"{quality}, rating_safe, 1girl", prompt, StringComparison.Ordinal);
    }

    /// <summary>Rule 12: age and hair are repeated early, because position shapes composition.</summary>
    [Fact]
    public void Pony_RepeatsAgeAndHairEarly_AsConcreteTokens()
    {
        var prompt = Pony().Positive;

        Assert.Contains("mature female", prompt, StringComparison.Ordinal);
        Assert.Contains("brown hair", prompt, StringComparison.Ordinal);
        Assert.Contains("hair bun", prompt, StringComparison.Ordinal);
        Assert.Contains("blue eyes", prompt, StringComparison.Ordinal);
        Assert.Contains("pale skin", prompt, StringComparison.Ordinal);
        // The raw age number is not a token Pony reads.
        Assert.DoesNotContain("50-year", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Pony_EndsWithAnExplicitViewAndStanceTag()
    {
        // Rule 5: without a view tag Pony defaults to overhead/top-down.
        Assert.EndsWith("full body, standing, front view, eye level", Pony().Positive, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rules 9 and 11 together: the negated muscle values contribute NOTHING to a Pony prompt. They are not
    /// reworded, not softened — the absence of a muscle token is already the honest statement.
    /// </summary>
    [Fact]
    public void Pony_OmitsTheInertNegatedValues_InsteadOfRewordingThem()
    {
        var prompt = Pony().Positive;

        Assert.DoesNotContain("no visible muscle", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no definition", prompt, StringComparison.OrdinalIgnoreCase);
        // The working token that IS emitted for the same axis family, per rule 11 (`chubby` works).
        Assert.Contains("chubby", prompt, StringComparison.Ordinal);
        // And the body region tokens, so "where the mass sits" survives.
        Assert.Contains("wide hips", prompt, StringComparison.Ordinal);
        Assert.Contains("large ass", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Pony_IsStructurallyValid_AndCarriesTheMinimalGuardNegative()
    {
        var compiled = Pony();

        // The compiled output passes the very validator the operator's reported prompt failed (§0 rule 5).
        Assert.True(compiled.IsValid);
        BodyPromptStructureValidator.RequireValid(compiled.Positive, BodyPromptFamily.Pony, ["Becky", "Dean"]);
        // Rule 8: short guard set, not a wall of negatives.
        Assert.Equal(BodyReferencePromptCompiler.PonyNegativeGuard, compiled.Negative);
    }

    /// <summary>Rules 2 and 4 are requirements, not defaults: the compiler refuses rather than inventing them.</summary>
    [Fact]
    public void Pony_RefusesRatherThanDefaultingTheRatingOrCountTag()
    {
        var noRating = Assert.Throws<InvalidOperationException>(
            () => BodyReferencePromptCompiler.Compile(Brief(), BodyPromptFamily.Pony, null, "1girl"));
        Assert.Contains("rule 2", noRating.Message, StringComparison.Ordinal);

        var noCount = Assert.Throws<InvalidOperationException>(
            () => BodyReferencePromptCompiler.Compile(Brief(), BodyPromptFamily.Pony, "rating_safe", null));
        Assert.Contains("rule 4", noCount.Message, StringComparison.Ordinal);
    }

    // ── SDXL: natural language, clothing in the positive, empty negative ───────────────────────────────────

    [Fact]
    public void Sdxl_IsANaturalLanguageBrief_WithNoTagVocabulary()
    {
        var prompt = Sdxl().Positive;

        Assert.Contains("Full-body photograph", prompt, StringComparison.Ordinal);
        // The age is a BAND in natural language too, never a numeral (see TheAgeIsABand_BothFamilies).
        Assert.Contains("a middle-aged woman", prompt, StringComparison.Ordinal);
        // §2.1: styling and camera cues belong at the tail.
        Assert.Contains("natural skin texture", prompt, StringComparison.Ordinal);
        Assert.Contains("35mm photograph", prompt, StringComparison.Ordinal);
        // The Pony tag vocabulary must not leak into the natural-language family.
        Assert.DoesNotContain("score_9", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("rating_safe", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// §2.2 rule 6: clothing is ALWAYS in the positive on an NSFW-trained checkpoint — it is the safety anchor. An
    /// unclothed reference says so explicitly rather than leaving the model to choose.
    /// </summary>
    [Fact]
    public void Sdxl_AlwaysStatesClothing_AndSaysSoExplicitlyWhenThereIsNone()
    {
        Assert.Contains("Wearing plain everyday clothing", Sdxl().Positive, StringComparison.Ordinal);

        var unclothed = Brief();
        unclothed.BodyState = SceneImageReferenceBodyState.Unclothed;
        unclothed.Clothing = null;
        Assert.Contains("Nude, fully unclothed", Sdxl(unclothed).Positive, StringComparison.Ordinal);
    }

    [Fact]
    public void Sdxl_UsesAnEmptyNegative_AndStaysInsideTheCharacterCeiling()
    {
        var compiled = Sdxl();

        // Author research: BigLust example workflows use no negative; the old guard set is superseded.
        Assert.Equal(string.Empty, compiled.Negative);
        Assert.True(compiled.Positive.Length <= BodyPromptStructureValidator.SdxlMaxChars,
            $"SDXL prompt was {compiled.Positive.Length} characters (§2.2 ceiling is {BodyPromptStructureValidator.SdxlMaxChars}).");
        Assert.True(compiled.IsValid);
    }

    // ── Shared contracts ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>§2.3 rule 2, both families: a name cannot be rendered, so it must never reach the prompt.</summary>
    [Fact]
    public void NeitherFamily_EmitsTheCharacterName()
    {
        Assert.DoesNotContain("Becky", Pony().Positive, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Becky", Sdxl().Positive, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The family is chosen by the caller from the model, and an unknown family is refused rather than guessed.
    /// </summary>
    [Fact]
    public void AnUnsupportedFamily_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => BodyReferencePromptCompiler.Compile(Brief(), (BodyPromptFamily)99, "rating_safe", "1girl"));

        Assert.Contains("Unsupported body prompt family", error.Message, StringComparison.Ordinal);
    }

    /// <summary>An incomplete brief has no honest prompt, and the brief's own error says what is missing.</summary>
    [Fact]
    public void AnIncompleteBrief_IsRefusedBeforeAnythingIsCompiled()
    {
        var brief = Brief();
        brief.Age = null;

        var error = Assert.Throws<InvalidOperationException>(
            () => BodyReferencePromptCompiler.Compile(brief, BodyPromptFamily.Sdxl));

        Assert.Contains("the age", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A body reference with no body facts is pointless, and says so rather than compiling an empty shell.</summary>
    [Fact]
    public void ABriefWithNoPickedBodyParts_IsRefused_NamingWhereToFixIt()
    {
        var brief = Brief();
        brief.Axes = new CharacterBodyAxes();

        var error = Assert.Throws<InvalidOperationException>(
            () => BodyReferencePromptCompiler.Compile(brief, BodyPromptFamily.Sdxl));

        Assert.Contains("no body parts picked", error.Message, StringComparison.Ordinal);
        Assert.Contains("body card", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Both families must compile the SAME brief — one semantic source, two dialects.</summary>
    [Fact]
    public void OneBrief_CompilesForBothFamilies_AndTheyDiffer()
    {
        var brief = Brief();
        var pony = Pony(brief);
        var sdxl = Sdxl(brief);

        Assert.NotEqual(pony.Positive, sdxl.Positive);
        Assert.Contains("wide hips", pony.Positive, StringComparison.Ordinal);
        Assert.Contains("wide hips", sdxl.Positive, StringComparison.Ordinal);
    }

    // ── Resolved-model routing, and the two Pony prerequisites derived from stated facts ──────────────────

    private static ResolvedImageModel Model(SceneImageModelFamily family) => new(
        "http://localhost",
        "/sdapi/v1/txt2img",
        300,
        null,
        family == SceneImageModelFamily.Pony ? "ponyDiffusionV6XL_v6StartWithThisOne" : "juggernautXL_v9",
        ImageContentPolicy.AdultAllowed,
        "Local",
        false,
        family,
        family == SceneImageModelFamily.Pony
            ? SceneImagePromptDialect.PonyV6Tags
            : SceneImagePromptDialect.SdxlNaturalLanguage,
        ImageProtocol.ComfyUi);

    /// <summary>
    /// The family is read off the RESOLVED model rather than re-derived from its name, so the operator's model pick
    /// is the single router — the same one the Model Manager already applies.
    /// </summary>
    [Fact]
    public void TheResolvedModel_SelectsTheDialect()
    {
        var pony = BodyReferencePromptCompiler.Compile(Brief(), Model(SceneImageModelFamily.Pony));
        var sdxl = BodyReferencePromptCompiler.Compile(Brief(), Model(SceneImageModelFamily.Sdxl));

        Assert.Equal(BodyPromptFamily.Pony, pony.Family);
        Assert.Equal(BodyPromptFamily.Sdxl, sdxl.Family);
        Assert.StartsWith(PonySceneImagePromptBuilder.PonyQualityTags, pony.Positive, StringComparison.Ordinal);
        Assert.Contains("Full-body photograph", sdxl.Positive, StringComparison.Ordinal);
    }

    /// <summary>
    /// There are TWO dialects, not one per model family. SDXL, FLUX and the API models all take the natural-language
    /// brief — the same split the scene-image compiler registry already makes. Refusing FLUX was wrong and shipped a
    /// body view that could never produce a prompt at all (the operator's own was configured on flux1-dev-fp8).
    /// </summary>
    [Theory]
    [InlineData(SceneImageModelFamily.Sdxl)]
    [InlineData(SceneImageModelFamily.Flux)]
    [InlineData(SceneImageModelFamily.Api)]
    [InlineData(SceneImageModelFamily.QwenImage21)]
    public void TheNaturalLanguageFamilies_AllCompileThePhotographicBrief(SceneImageModelFamily family)
    {
        var compiled = BodyReferencePromptCompiler.Compile(Brief(), Model(family));

        Assert.Equal(BodyPromptFamily.Sdxl, compiled.Family);
        Assert.Contains("Full-body photograph", compiled.Positive, StringComparison.Ordinal);
        Assert.DoesNotContain("score_9", compiled.Positive, StringComparison.Ordinal);
        // Those families carry no negative (BFL: most FLUX models do not support one), which is not a defect.
        Assert.Equal(string.Empty, compiled.Negative);
        // Provenance records the MODEL family, not the dialect it happens to share.
        Assert.Equal(family, compiled.ModelFamily);
        Assert.True(compiled.IsValid);
    }

    /// <summary>
    /// A family that IS configured but has no branch here is reported as a COMPILER GAP, not as a missing setting
    /// (operator report, 2026-09-23: "again reload prmpt DOES nothing").
    ///
    /// Adding a family to the enum without extending this method used to fall into a catch-all that said "no
    /// scene-image family set in Model Manager" — which sent the operator to Model Manager to fix a setting that was
    /// already correct. The two refusals must stay distinct because the remedies are opposite: one is a setting, the
    /// other is a missing branch.
    /// </summary>
    [Fact]
    public void AFamilyWithNoBranch_IsReportedAsACompilerGap_NotAsAMissingSetting()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => BodyReferencePromptCompiler.Compile(Brief(), Model((SceneImageModelFamily)999)));

        Assert.Contains("no body-reference prompt branch yet", error.Message, StringComparison.Ordinal);
        Assert.Contains("FamilyFor", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("no scene-image family set", error.Message, StringComparison.Ordinal);
    }

    /// <summary>An unconfigured model has no dialect to compile, and says so with the remedy.</summary>
    [Fact]
    public void AModelWithNoConfiguredFamily_IsRefusedNamingTheRemedy()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => BodyReferencePromptCompiler.Compile(Brief(), Model(SceneImageModelFamily.Unknown)));

        Assert.Contains("no scene-image family set", error.Message, StringComparison.Ordinal);
        Assert.Contains("Body view settings", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Pony rating is DERIVED from the body state instead of being passed in or hardcoded. The old path
    /// hardcoded <c>rating_explicit</c> for every asset, so a clothed reference asked for explicit content.
    /// </summary>
    [Fact]
    public void TheRatingIsDerivedFromTheBodyState_NotHardcoded()
    {
        var clothed = BodyReferencePromptCompiler.Compile(Brief(), Model(SceneImageModelFamily.Pony));
        Assert.Contains(", rating_safe, 1girl", clothed.Positive, StringComparison.Ordinal);
        Assert.DoesNotContain("rating_explicit", clothed.Positive, StringComparison.Ordinal);

        var unclothedBrief = Brief();
        unclothedBrief.BodyState = SceneImageReferenceBodyState.Unclothed;
        unclothedBrief.Clothing = null;
        var unclothed = BodyReferencePromptCompiler.Compile(unclothedBrief, Model(SceneImageModelFamily.Pony));

        Assert.Contains(", rating_explicit, 1girl", unclothed.Positive, StringComparison.Ordinal);
    }

    /// <summary>Rule 4's count tag comes from the stated gender, and an unstated one is refused rather than assumed.</summary>
    [Fact]
    public void TheCountTagFollowsTheStatedGender()
    {
        var male = Brief();
        male.Gender = "Male";
        male.Axes.BustSize = null;

        Assert.Contains("1boy", BodyReferencePromptCompiler.Compile(male, Model(SceneImageModelFamily.Pony)).Positive, StringComparison.Ordinal);
        Assert.Contains("1girl", BodyReferencePromptCompiler.Compile(Brief(), Model(SceneImageModelFamily.Pony)).Positive, StringComparison.Ordinal);
    }

    /// <summary>
    /// A fact reaching Pony twice spends the attention budget twice and was measured to have no extra effect. Both
    /// sources are legitimate (fat distribution and rear volume genuinely overlap), so the repeat is removed at the
    /// tag level rather than by dropping one of the operator's picks.
    /// </summary>
    [Fact]
    public void Pony_DropsRepeatedTokens_KeepingTheFirstOccurrence()
    {
        var positive = Pony().Positive;
        var occurrences = positive.Split(", ").Count(tag => tag == "large ass");

        Assert.Equal(1, occurrences);
        // Flattening must not disturb the load-bearing order it is applied to.
        Assert.StartsWith(PonySceneImagePromptBuilder.PonyQualityTags, positive, StringComparison.Ordinal);
        Assert.EndsWith("full body, standing, front view, eye level", positive, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pick or a free-text detail that Pony cannot render is REPORTED, not swallowed. "My pick did nothing" and
    /// "the model ignored it" must not look the same to the operator.
    /// </summary>
    [Fact]
    public void Pony_ReportsWhatItCannotCarry_InsteadOfSilentlyDroppingIt()
    {
        var brief = Brief();
        brief.Tattoos = "tattoo of a tree on left calf";
        brief.Axes.MuscleMass = "no visible muscle";

        var compiled = Pony(brief);

        Assert.Contains(compiled.Omissions, omission => omission.Contains("tattoos", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(compiled.Omissions, omission => omission.Contains("no visible muscle", StringComparison.Ordinal));
        // The omissions are informational: the prompt itself still compiles and passes the validator.
        Assert.True(compiled.IsValid);

        // SDXL carries every one of them, so it reports no omissions.
        Assert.Empty(Sdxl(brief).Omissions);
    }

    /// <summary>The free-text body detail belongs in the natural-language brief and must appear there.</summary>
    [Fact]
    public void Sdxl_CarriesTheCardsFreeTextBodyDetail()
    {
        var brief = Brief();
        brief.Tattoos = "tattoo of a tree on left calf";
        brief.ScarsMarks = "a faded scar across the right knee";

        var prompt = Sdxl(brief).Positive;

        Assert.Contains("tattoo of a tree on left calf", prompt, StringComparison.Ordinal);
        Assert.Contains("a faded scar across the right knee", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// A HAND-TYPED axis value ("(custom…)" on the body card) is carried by SDXL as written — natural language can
    /// use the operator's own wording, which is the whole point of the override. It must not throw: the picker
    /// invites typing, so a typed value that broke generation would make the feature a trap.
    /// </summary>
    [Fact]
    public void AHandTypedAxisValue_IsCarriedBySdxl_AndNotRefused()
    {
        var brief = Brief();
        brief.Axes.Silhouette = "stocky, broad through the chest from farm work";

        var prompt = Sdxl(brief).Positive;

        Assert.Contains("stocky, broad through the chest from farm work", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The age is stated as a maturity BAND for both families, never as a numeral (operator report, 2026-09-23: "I
    /// thought 40-year was not going to be included, was to be replaced with something else").
    ///
    /// A bare age is nearly information-free for an image model — the same reasoning the body taxonomy records for
    /// weight ("Build axes carry the signal; the number does not") — and Pony cannot express a number at all. Every
    /// proven prompt in this repo states a band: the SDXL canon's own example is "a middle-aged man and a middle-aged
    /// woman", and the beach/touch/NSFW proofs all say "middle-aged". The two families now take their band from ONE
    /// table, so they cannot describe the same person differently.
    /// </summary>
    [Theory]
    [InlineData("40", "a middle-aged woman", "mature female")]
    [InlineData("50", "a middle-aged woman", "mature female")]
    [InlineData("65", "an older woman", "old woman")]
    [InlineData("88", "an older woman", "old woman")]
    public void TheAgeIsABand_BothFamilies_AndNeverANumeral(string age, string sdxlPhrase, string ponyToken)
    {
        var brief = Brief();
        brief.Age = age;

        var sdxl = Sdxl(brief).Positive;
        var pony = Pony(brief).Positive;

        Assert.Contains(sdxlPhrase, sdxl, StringComparison.Ordinal);
        Assert.Contains(ponyToken, pony, StringComparison.Ordinal);

        // The numeral is nowhere in either prompt.
        Assert.DoesNotContain($"{age}-year-old", sdxl, StringComparison.Ordinal);
        Assert.DoesNotContain(age, sdxl, StringComparison.Ordinal);
        Assert.DoesNotContain(age, pony, StringComparison.Ordinal);
    }

    /// <summary>
    /// Below the lowest band the age is OMITTED, not guessed, and the noun phrase still reads grammatically. A numeral
    /// is not a fallback for a missing band.
    /// </summary>
    [Fact]
    public void AnAgeBelowEveryBand_IsOmitted_WithTheArticleStillCorrect()
    {
        var brief = Brief();
        brief.Age = "29";

        var sdxl = Sdxl(brief).Positive;

        Assert.Contains("Full-body photograph of a woman,", sdxl, StringComparison.Ordinal);
        Assert.DoesNotContain("29", sdxl, StringComparison.Ordinal);
        Assert.DoesNotContain("year-old", sdxl, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pubic hair is an UNCLOTHED-only detail and must not reach a CLOTHED prompt. The card stores it once and both
    /// states share that card, so emitting it unconditionally put it in the clothed base — contradicting the state the
    /// operator picked, and pulling the render toward nudity with no negative prompt to push back (the SDXL family's
    /// negative is empty by design). Details visible on a clothed person stay in both states.
    /// </summary>
    [Fact]
    public void Clothed_OmitsPubicHair_WhileUnclothedCarriesIt()
    {
        var clothed = Brief();
        clothed.BodyState = SceneImageReferenceBodyState.Clothed;
        clothed.Clothing = "plain everyday clothing";
        clothed.PubicHair = "small triangle pubic hair";
        clothed.Tattoos = "tattoo of a tree on left calf";
        clothed.BodyHair = "fine body hair";

        var unclothed = Brief();
        unclothed.BodyState = SceneImageReferenceBodyState.Unclothed;
        unclothed.Clothing = string.Empty;
        unclothed.PubicHair = "small triangle pubic hair";
        unclothed.Tattoos = "tattoo of a tree on left calf";
        unclothed.BodyHair = "fine body hair";

        var clothedPrompt = Sdxl(clothed).Positive;
        var unclothedPrompt = Sdxl(unclothed).Positive;

        Assert.DoesNotContain("pubic hair", clothedPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pubic hair", unclothedPrompt, StringComparison.OrdinalIgnoreCase);

        // A detail that IS visible on a clothed person survives in both.
        Assert.Contains("tattoo of a tree on left calf", clothedPrompt, StringComparison.Ordinal);
        Assert.Contains("tattoo of a tree on left calf", unclothedPrompt, StringComparison.Ordinal);
        Assert.Contains("fine body hair", clothedPrompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// ...and Pony gets NOTHING for the same value, because a tag dialect degrades on arbitrary prose (rule 3). The
    /// omission is REPORTED with its reason, so the operator can tell "Pony cannot carry this" from "the model
    /// ignored it".
    /// </summary>
    [Fact]
    public void AHandTypedAxisValue_IsOmittedForPony_WithItsReasonReported()
    {
        var brief = Brief();
        brief.Axes.Silhouette = "stocky, broad through the chest from farm work";

        var compiled = Pony(brief);

        Assert.DoesNotContain("farm work", compiled.Positive, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            compiled.Omissions,
            omission => omission.Contains("stocky, broad through the chest from farm work", StringComparison.Ordinal));
        Assert.True(compiled.IsValid);
    }

    /// <summary>
    /// A hand-typed value that ends in its own comma must NOT produce an empty join (operator report, 2026-09-22:
    /// the body card stored "tattoo of a tree on left calve," and the compiled brief contained "calve,,". The
    /// structural guard then refused the ENTIRE render — so pressing Generate did nothing but show a refusal, over
    /// punctuation the composer itself owned).
    ///
    /// The composer owns the separators around a value: a stray comma in the operator's wording is not a request for
    /// a second join. It is normalised away where the join happens.
    /// </summary>
    [Theory]
    [InlineData("tattoo of a tree on left calve,")]
    [InlineData(", tattoo of a tree on left calve")]
    [InlineData("  tattoo of a tree on left calve ,  ")]
    [InlineData("tattoo of a tree on left calve;;")]
    public void AHandTypedValueWithItsOwnSeparators_DoesNotProduceAnEmptyJoin(string typed)
    {
        var brief = Brief();
        brief.Tattoos = typed;

        // A SECOND detail phrase, so the join between two of them is what is under test. It must be a detail that is
        // valid in BOTH states: pubic hair is unclothed-only (see Clothed_OmitsPubicHair), so pairing the tattoo with
        // it would make this test depend on the state gate instead of on separator hygiene.
        brief.ScarsMarks = "a faded scar on the right knee";

        var compiled = Sdxl(brief);

        Assert.DoesNotContain(",,", compiled.Positive, StringComparison.Ordinal);
        Assert.DoesNotContain(", ,", compiled.Positive, StringComparison.Ordinal);
        Assert.DoesNotContain("Body: ,", compiled.Positive, StringComparison.Ordinal);
        Assert.Contains("tattoo of a tree on left calve, a faded scar on the right knee", compiled.Positive, StringComparison.Ordinal);

        // The real point: the render is no longer refused over the punctuation.
        Assert.True(compiled.IsValid, string.Join(" | ", compiled.Findings.Select(finding => $"{finding.Code}: {finding.Message}")));
    }

    /// <summary>A value that is nothing BUT separators contributes no slot at all, rather than an empty one.</summary>
    [Fact]
    public void AValueThatIsOnlySeparators_ContributesNothing()
    {
        var brief = Brief();
        brief.Tattoos = " , ,, ";

        var compiled = Sdxl(brief);

        Assert.DoesNotContain(",,", compiled.Positive, StringComparison.Ordinal);
        Assert.True(compiled.IsValid, string.Join(" | ", compiled.Findings.Select(finding => $"{finding.Code}: {finding.Message}")));
    }

    /// <summary>
    /// The same hazard in the SUBJECT line: the template fields (hair, eyes, skin) are joined by the composer too, so
    /// a stray separator in one of them must not break the sentence either.
    /// </summary>
    [Fact]
    public void AStraySeparatorInATemplateField_DoesNotProduceAnEmptyJoin()
    {
        var brief = Brief();
        brief.HairStyle = "bun,";
        brief.EyeColour = ",blue";

        var compiled = Sdxl(brief);

        Assert.DoesNotContain(",,", compiled.Positive, StringComparison.Ordinal);
        Assert.Contains("bun hairstyle", compiled.Positive, StringComparison.Ordinal);
        Assert.True(compiled.IsValid, string.Join(" | ", compiled.Findings.Select(finding => $"{finding.Code}: {finding.Message}")));
    }
}
