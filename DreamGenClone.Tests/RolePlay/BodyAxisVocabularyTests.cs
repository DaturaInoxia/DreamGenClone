using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The per-family body vocabulary and the structural prompt validator (B-122).
///
/// The vocabulary test proves COVERAGE: every value the operator can pick has a rendering for each family, so a
/// raw operator string can never reach a model. The validator tests prove the rules that the operator's real
/// prompt violated are actually enforced — a name, an empty join, and two negations that Pony ignores.
/// </summary>
public sealed class BodyAxisVocabularyTests
{
    /// <summary>
    /// Coverage, proved rather than sampled: every value of every covered catalog field has a rendering. A new
    /// catalog value without one fails here instead of silently reaching the model as raw text.
    /// </summary>
    [Fact]
    public void EveryValueOfEveryCoveredField_HasARendering()
    {
        var missing = new List<string>();

        foreach (var (field, values) in CatalogValues())
        {
            Assert.Contains(field, BodyAxisVocabulary.CoveredFields);
            var axis = Enum.Parse<BodyAxis>(field);
            foreach (var value in values)
            {
                if (!BodyAxisVocabulary.HasRendering(axis, value))
                {
                    missing.Add($"{field}:'{value}'");
                }
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// A value is NOT a key: "Average" is both a bust volume and a skeletal hip width, and the two render differently
    /// (operator report, 2026-09-23 — the compiled prompt described hips and rear but never the bust).
    ///
    /// The vocabulary was one flat table keyed by value alone, built with indexer initializers, so the hips entry
    /// silently OVERWROTE the bust one: `Require("Average")` always returned "average hips" and "an average bust" was
    /// unreachable dead data. The axis now selects the entry.
    /// </summary>
    [Fact]
    public void AValueTwoAxesShare_RendersDifferentlyOnEach_AndIsRefusedWithoutAnAxis()
    {
        var bust = BodyAxisVocabulary.Require(BodyAxis.BustSize, "Average");
        var hips = BodyAxisVocabulary.Require(BodyAxis.HipSize, "Average");

        Assert.Equal("an average bust", bust.SdxlPhrase);
        Assert.Equal("average hips", hips.SdxlPhrase);
        Assert.NotEqual(bust, hips);

        // The bust rendering must be REACHABLE — the whole point: this is what the collision had made dead.
        Assert.Equal("medium breasts", bust.PonyTags);

        // And with no axis to disambiguate it, the lookup REFUSES rather than returning whichever entry won.
        var ambiguous = Assert.Throws<InvalidOperationException>(() => BodyAxisVocabulary.Require("Average"));
        Assert.Contains("more than one axis", ambiguous.Message, StringComparison.Ordinal);
        Assert.Contains("BodyAxis", ambiguous.Message, StringComparison.Ordinal);
    }

    /// <summary>An unambiguous value still resolves without an axis — the axis is only required where it decides.</summary>
    [Fact]
    public void AnUnambiguousValue_StillResolvesFromTheValueAlone()
    {
        Assert.Equal("chubby", BodyAxisVocabulary.Require("a little soft, slight roundness").PonyTags);
        Assert.True(BodyAxisVocabulary.HasRendering("hourglass"));
    }

    /// <summary>
    /// Operator request 2026-09-23: "all of the body building dropdowns are for a typical female body — I need the
    /// equivalent for typical male". The male catalogs are offered for a male character, and every one of their values
    /// renders for both families — before these entries existed, the male silhouettes and male fat distributions were
    /// PICKABLE and contributed nothing to a prompt, silently.
    /// </summary>
    [Fact]
    public void MaleValues_RenderForBothFamilies_AndTheShapeLineSaysChest()
    {
        Assert.Equal("an average chest", BodyAxisVocabulary.Require(BodyAxis.BustSize, "average chest").SdxlPhrase);
        Assert.Equal("pectorals, muscular", BodyAxisVocabulary.Require(BodyAxis.BustSize, "developed pectorals").PonyTags);
        Assert.Contains(
            "broader than the waist",
            BodyAxisVocabulary.Require(BodyAxis.Silhouette, "V-taper, shoulders clearly broader than the waist").SdxlPhrase,
            StringComparison.Ordinal);
        Assert.Contains(
            "belly carried forward",
            BodyAxisVocabulary.Require(BodyAxis.FatDistribution, "belly carried forward").SdxlPhrase,
            StringComparison.Ordinal);
        Assert.Equal("muscular, well-developed glutes", BodyAxisVocabulary.Require(BodyAxis.ButtSize, "muscular, well-developed glutes").SdxlPhrase);

        // The composed shape line says CHEST on a male card, and a female card's wording is untouched.
        var male = new CharacterBodyAxes { ChestLabel = "chest", BustSize = "Average", HipSize = "Average" };
        Assert.Contains("chest Average", male.Compose(), StringComparison.Ordinal);

        var female = new CharacterBodyAxes { BustSize = "Average", HipSize = "Average" };
        Assert.Contains("bust Average", female.Compose(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A value with no rendering is refused, never passed through: forwarding the operator's raw string is how an
    /// unclear value becomes an unclear prompt.
    /// </summary>
    [Fact]
    public void AnUnrenderedValue_IsRefused_AndNamesTheFix()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => BodyAxisVocabulary.Require("something the operator typed"));

        Assert.Contains("has no per-family rendering", error.Message, StringComparison.Ordinal);
        Assert.Contains("BodyAxisVocabulary", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pony rule 9: a negation is an IGNORED token, so the rendering must be empty and must say why — not a
    /// rephrased negation that still occupies attention and still changes nothing.
    /// </summary>
    [Theory]
    [InlineData("no visible muscle")]
    [InlineData("soft, no definition")]
    public void TheNegatedMuscleValues_AreOmittedForPony_WithAReason(string value)
    {
        var rendering = BodyAxisVocabulary.Require(value);

        Assert.True(rendering.IsOmittedForPony);
        Assert.NotNull(rendering.PonyOmissionReason);
        Assert.Contains("rule 9", rendering.PonyOmissionReason!, StringComparison.OrdinalIgnoreCase);
        // The SDXL family has no such rule, so it still gets a phrase.
        Assert.False(string.IsNullOrWhiteSpace(rendering.SdxlPhrase));
    }

    /// <summary>
    /// Pony rule 11: where no reliable token exists, the honest rendering is EMPTY with a stated reason. Emitting
    /// filler ("average", "evenly distributed") is the failure the rule names.
    /// </summary>
    [Fact]
    public void VagueValues_AreOmittedForPony_RatherThanRenderedAsFiller()
    {
        foreach (var vague in new[] { "evenly distributed", "average weight", "average " , "a little soft, slight roundness" })
        {
            if (!BodyAxisVocabulary.HasRendering(vague))
            {
                continue;
            }

            var rendering = BodyAxisVocabulary.Require(vague);
            if (rendering.IsOmittedForPony)
            {
                Assert.NotNull(rendering.PonyOmissionReason);
            }
        }

        // The one vague value that DOES map cleanly: rule 11 names `chubby` as a working token.
        Assert.Equal("chubby", BodyAxisVocabulary.Require("a little soft, slight roundness").PonyTags);
    }

    /// <summary>Silhouette has no single booru tag, so it must render as a token combination, not an invented tag.</summary>
    [Fact]
    public void Silhouettes_RenderAsTokenCombinations_NotAnInventedHourglassTag()
    {
        var hourglass = BodyAxisVocabulary.Require("hourglass");
        Assert.Contains("wide hips", hourglass.PonyTags, StringComparison.Ordinal);
        Assert.Contains("narrow waist", hourglass.PonyTags, StringComparison.Ordinal);
        Assert.DoesNotContain("hourglass", hourglass.PonyTags, StringComparison.OrdinalIgnoreCase);

        var pear = BodyAxisVocabulary.Require("pear, hips and thighs fuller than the upper body");
        Assert.Contains("thick thighs", pear.PonyTags, StringComparison.Ordinal);
    }

    /// <summary>No rendering may itself be a negation — the rule the validator enforces downstream.</summary>
    [Fact]
    public void NoPonyRendering_IsANegation()
    {
        // Keyed by RENDERING, not by value: a value can belong to more than one axis, and it is each rendering that
        // must obey the rule.
        foreach (var rendering in BodyAxisVocabulary.AllRenderings)
        {
            if (rendering.IsOmittedForPony)
            {
                continue;
            }

            var findings = BodyPromptStructureValidator.Validate(
                rendering.PonyTags, BodyPromptFamily.Pony);

            Assert.DoesNotContain(findings, finding => finding.Code == BodyPromptStructureValidator.NegationInPositive);
        }
    }

    private static IEnumerable<(string Field, IEnumerable<string> Values)> CatalogValues()
    {
        yield return ("BodyBuild", PhysicalAttributesCatalog.BodyBuilds);
        yield return ("Silhouette", PhysicalAttributesCatalog.Silhouettes);
        yield return ("Adiposity", PhysicalAttributesCatalog.AdiposityLevels);
        yield return ("FatDistribution", PhysicalAttributesCatalog.FatDistributions);
        yield return ("MuscleMass", PhysicalAttributesCatalog.MuscleMasses);
        yield return ("MuscleDefinition", PhysicalAttributesCatalog.MuscleDefinitions);
        yield return ("BustSize", PhysicalAttributesCatalog.BustSizes);
        yield return ("WaistSize", PhysicalAttributesCatalog.WaistSizes);
        yield return ("HipSize", PhysicalAttributesCatalog.HipSizes);
        yield return ("ButtSize", PhysicalAttributesCatalog.ButtSizes);

        // The MALE catalogs, on the same axes ("Chest" is still the BustSize axis; only the values and the label follow
        // the gender). They are enumerated here because their absence from this list is exactly why the male silhouettes
        // and male fat distributions were pickable and contributed NOTHING to a prompt: the coverage test only looked at
        // the female lists, so an unrendered male value failed nowhere (found 2026-09-23).
        yield return ("BodyBuild", PhysicalAttributesCatalog.MaleBodyBuilds);
        yield return ("Silhouette", PhysicalAttributesCatalog.MaleSilhouettes);
        yield return ("FatDistribution", PhysicalAttributesCatalog.MaleFatDistributions);
        yield return ("BustSize", PhysicalAttributesCatalog.MaleChestSizes);
        yield return ("WaistSize", PhysicalAttributesCatalog.MaleWaistSizes);
        yield return ("HipSize", PhysicalAttributesCatalog.MaleHipSizes);
        yield return ("ButtSize", PhysicalAttributesCatalog.MaleButtSizes);
    }
}

/// <summary>
/// Structural prompt validation (§0 rule 5). The operator's real prompt is the fixture: it must fail, and it must
/// fail for the three specific documented reasons.
/// </summary>
public sealed class BodyPromptStructureValidatorTests
{
    /// <summary>
    /// The exact prompt the operator reported. It contains a name, an empty join, and negations — and on the Pony
    /// path two of those clauses are tokens the model ignores outright.
    /// </summary>
    private const string ReportedPrompt =
        "Full-body photograph of Becky, head to feet, standing straight and facing the camera: average frame, "
        + "bottom hourglass, hips fuller than bust with a clear waist, a little soft, slight roundness, evenly "
        + "distributed, no visible muscle, soft, no definition, bust Full, waist Soft, hips Wide, rear full, "
        + "5'8\", 150 lbs, Fair, Smooth, tattoo of a tree on left calve,, small triangle pubic hair.";

    [Fact]
    public void TheReportedPrompt_FailsForItsRealDefects()
    {
        var findings = BodyPromptStructureValidator.Validate(
            ReportedPrompt, BodyPromptFamily.Pony, ["Becky"]);

        var codes = findings.Select(finding => finding.Code).ToArray();
        Assert.Contains(BodyPromptStructureValidator.ForbiddenToken, codes);
        Assert.Contains(BodyPromptStructureValidator.EmptyJoin, codes);
        Assert.Contains(BodyPromptStructureValidator.NegationInPositive, codes);
    }

    /// <summary>
    /// The negation rule is PONY-ONLY. The SDXL family has no such documented behaviour, so flagging "no" there
    /// would be inventing a rule — the validator must not.
    /// </summary>
    [Fact]
    public void ANegation_IsOnlyADefectOnThePonyPath()
    {
        const string prompt = "A full-body photograph of a woman with no visible muscle definition, standing.";

        Assert.Empty(BodyPromptStructureValidator.Validate(prompt, BodyPromptFamily.Sdxl));
        Assert.Contains(
            BodyPromptStructureValidator.Validate(prompt, BodyPromptFamily.Pony),
            finding => finding.Code == BodyPromptStructureValidator.NegationInPositive);
    }

    [Fact]
    public void AnEmptyJoin_IsCaught_AndExplainsItIsACompositionDefect()
    {
        var findings = BodyPromptStructureValidator.Validate(
            "1girl, slim, , standing", BodyPromptFamily.Pony);

        var finding = Assert.Single(findings, f => f.Code == BodyPromptStructureValidator.EmptyJoin);
        Assert.Contains("Fix the composition", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPrompt_IsCaught()
    {
        Assert.Equal(
            BodyPromptStructureValidator.EmptyPrompt,
            Assert.Single(BodyPromptStructureValidator.Validate("   ", BodyPromptFamily.Sdxl)).Code);
    }

    [Fact]
    public void AnOverLengthPrompt_IsCaught_NamingTheFamilyCeiling()
    {
        var prompt = string.Join(", ", Enumerable.Repeat("slim", 200));

        var finding = Assert.Single(
            BodyPromptStructureValidator.Validate(prompt, BodyPromptFamily.Pony),
            f => f.Code == BodyPromptStructureValidator.OverLength);

        Assert.Contains(BodyPromptStructureValidator.PonyMaxChars.ToString(), finding.Message, StringComparison.Ordinal);
    }

    /// <summary>A name inside a longer word is not a name match, so the check stays honest rather than a substring scan.</summary>
    [Fact]
    public void ForbiddenTokenMatching_IsWholeWord()
    {
        Assert.Empty(BodyPromptStructureValidator.Validate(
            "1girl, beckoning pose, slim", BodyPromptFamily.Pony, ["Becky"]));

        Assert.Single(
            BodyPromptStructureValidator.Validate("1girl, Becky, slim", BodyPromptFamily.Pony, ["Becky"]),
            f => f.Code == BodyPromptStructureValidator.ForbiddenToken);
    }

    [Fact]
    public void ACleanPonyPrompt_HasNoFindings()
    {
        Assert.Empty(BodyPromptStructureValidator.Validate(
            "score_9, score_8_up, score_7_up, rating_safe, 1girl, mature female, brown hair, bun hairstyle, "
            + "blue eyes, chubby, wide hips, narrow waist, full body, front view, standing, full body",
            BodyPromptFamily.Pony));
    }

    /// <summary>Every defect is reported at once, so fixing a prompt is not a game of whack-a-mole.</summary>
    [Fact]
    public void RequireValid_NamesEveryFindingAtOnce()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => BodyPromptStructureValidator.RequireValid(
                ReportedPrompt, BodyPromptFamily.Pony, ["Becky"]));

        Assert.Contains(BodyPromptStructureValidator.ForbiddenToken, error.Message, StringComparison.Ordinal);
        Assert.Contains(BodyPromptStructureValidator.EmptyJoin, error.Message, StringComparison.Ordinal);
        Assert.Contains(BodyPromptStructureValidator.NegationInPositive, error.Message, StringComparison.Ordinal);
    }
}
