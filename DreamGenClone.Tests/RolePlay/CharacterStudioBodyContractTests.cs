using System.Text.RegularExpressions;

using System.Text.RegularExpressions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Body-section invariants of the Character Studio (B-122 Phase 0). These are the same class of guarantee the
/// Faces contract tests hold, applied to the flow the body target needs to be drivable at all:
///
/// <list type="bullet">
/// <item>the body card's inputs are bound to the card's own properties — never a dictionary indexer, which is what
/// killed the Faces tab with the "Reload" error UI (B-121 note 009);</item>
/// <item>the body build is a SEPARATE build from the face build, so neither tab can render the other's plan;</item>
/// <item>the card's wording and its <c>[DECIDE]</c> marking come from the domain definition, not a second copy;</item>
/// <item>the card is saved through the service under the version the editor loaded, so a stale editor cannot
/// overwrite newer work;</item>
/// <item>nothing here offers a batch, a sweep or "generate all": one request is one image (B122-021).</item>
/// </list>
/// </summary>
public sealed class CharacterStudioBodyContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string CharacterStudioPath =
        Path.Combine(Root, "DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor");

    private static readonly string Source = File.ReadAllText(CharacterStudioPath);

    /// <summary>The Body section only, so an assertion cannot be satisfied by the Faces markup.</summary>
    private static readonly string BodySection = Slice(
        Source,
        "case \"Body\":",
        "case \"LoRA images\":");

    /// <summary>
    /// The body card mirrors the template's body axes (operator decision, 2026-09-22): the template is the start
    /// point and the tweaks live on the card, so the card carries the same vocabulary.
    ///
    /// The operator replaced the datalists on 2026-09-22. A datalist is filtered by whatever is already in the box,
    /// so a field that HAD a value showed an empty popup — the reported symptom. These are selects, which always
    /// show the whole catalog, plus a "(custom…)" option that opens a text box: a catalog pick and a typed override
    /// are two visible choices, and an off-catalog descriptor stays typeable. The contract this pins is therefore
    /// "a dropdown that still allows typing", not "a closed list".
    /// </summary>
    [Fact]
    public void BodyCard_OffersTheTemplatesBodyAxes_AsDropdownsThatStillAllowTyping()
    {
        // The markup: a select with the custom escape hatch — never the filtered datalist it replaced.
        var picks = Slice(BodySection, "Body parts", "@BodyCardLabel(CharacterBodyCardField.BodyShape)");
        Assert.Contains("<select", picks, StringComparison.Ordinal);
        Assert.Contains("CustomAxisValue", picks, StringComparison.Ordinal);
        Assert.Contains("(custom", picks, StringComparison.Ordinal);
        Assert.Contains("BodyAxisPickers", picks, StringComparison.Ordinal);
        Assert.DoesNotContain("<datalist", picks, StringComparison.Ordinal);
        Assert.Contains("ComposeBodyAxesAsync", BodySection, StringComparison.Ordinal);

        // Every axis is on the ONE picker list, with both a reader and a writer — so a pick cannot exist in the form
        // but be unreachable from, or unwritable to, the card.
        foreach (var axis in new[]
                 {
                     "BodyBuild", "Silhouette", "Adiposity", "FatDistribution", "MuscleMass", "MuscleDefinition",
                     "BustSize", "WaistSize", "HipSize", "ButtSize"
                 })
        {
            Assert.Contains($"axes.{axis}", Source, StringComparison.Ordinal);
            Assert.Contains($"axes.{axis} = value", Source, StringComparison.Ordinal);
        }

        // The catalogs come from the domain, so the card cannot drift from the vocabulary the template editor reads.
        foreach (var catalog in new[]
                 {
                     "PhysicalAttributesCatalog.BodyBuilds", "PhysicalAttributesCatalog.AdiposityLevels",
                     "PhysicalAttributesCatalog.MuscleMasses", "PhysicalAttributesCatalog.MuscleDefinitions",
                     "PhysicalAttributesCatalog.BustSizes", "PhysicalAttributesCatalog.WaistSizes",
                     "PhysicalAttributesCatalog.HipSizes", "PhysicalAttributesCatalog.ButtSizes"
                 })
        {
            Assert.Contains(catalog, Source, StringComparison.Ordinal);
        }

        // Silhouette and fat distribution are the gender-dependent pair, so they are offered from the resolved sets.
        Assert.Contains("SilhouetteOptions", Source, StringComparison.Ordinal);
        Assert.Contains("FatDistributionOptions", Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Operator report 2026-09-23 (Dean, whose stored Gender is "Male"): "all of the body building dropdowns are for a
    /// typical female body, but the character template already has the gender set to male".
    ///
    /// The cause was the LOOKUP, not the catalogs: the studio matched the route id against the scenario character's own
    /// id, while a page opened from the character template carries the TemplateId — so the character was never found,
    /// Gender stayed null, and every gender-dependent picker fell back to the female set silently.
    /// </summary>
    [Fact]
    public void TheCharacterGender_IsResolvedFromEitherTheCharacterIdOrItsTemplateId()
    {
        Assert.Contains("string.Equals(c.Id, characterId, StringComparison.Ordinal)", Source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(c.TemplateId, characterId, StringComparison.Ordinal)", Source, StringComparison.Ordinal);

        // And WHICH set is in use is stated on the form, so a set that disagrees with the template's gender is visible
        // before a render is spent rather than discovered in the image.
        Assert.Contains("@BodyAxisSourceNote", Source, StringComparison.Ordinal);
        Assert.Contains("Body values shown for MALE characters", Source, StringComparison.Ordinal);

        // Every axis with a male equivalent offers it: build, chest (the BustSize axis), waist, hips and rear, beside the
        // silhouette and distribution pair that already had one.
        foreach (var maleCatalog in new[]
                 {
                     "PhysicalAttributesCatalog.MaleBodyBuilds", "PhysicalAttributesCatalog.MaleChestSizes",
                     "PhysicalAttributesCatalog.MaleWaistSizes", "PhysicalAttributesCatalog.MaleHipSizes",
                     "PhysicalAttributesCatalog.MaleButtSizes"
                 })
        {
            Assert.Contains(maleCatalog, Source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BodyCard_InputsAreBoundToCardProperties_AndNeverToADictionaryIndexer()
    {
        Assert.DoesNotContain("@bind=\"_bodyCard[", BodySection, StringComparison.Ordinal);
        Assert.DoesNotContain("@bind=\"_bodyCard!.", BodySection, StringComparison.Ordinal);

        foreach (var field in new[]
                 {
                     "BodyShape", "HeightBuild", "Skin", "BodyHair", "Tattoos", "ScarsMarks", "PubicHair"
                 })
        {
            Assert.Contains($"@bind=\"_bodyCard.{field}\"", BodySection, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Studio_KeepsTheFaceAndBodyBuildsApart_InsteadOfTakingAnyBuild()
    {
        Assert.Contains(
            "_build = builds.FirstOrDefault(build => build.TargetKind == CharacterIdentityTargetKind.Face)",
            Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "_bodyBuild = builds.FirstOrDefault(build => build.TargetKind == CharacterIdentityTargetKind.Body)",
            Source,
            StringComparison.Ordinal);

        // The any-kind form is what made the Faces tab able to render a body build's steps.
        Assert.DoesNotContain("_build = builds.FirstOrDefault();", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void BodyCard_LabelsAndDecisionMarkingComeFromTheDomainDefinition()
    {
        Assert.Contains("CharacterBodyCardFields.Require(field).Label", Source, StringComparison.Ordinal);
        Assert.Contains("CharacterBodyCardFields.Require(field).RequiresDecision", Source, StringComparison.Ordinal);
        Assert.Contains("[DECIDE]", BodySection, StringComparison.Ordinal);

        // The mark is driven by the definition's flag, not by a list of field names written into the markup.
        Assert.Contains("BodyCardRequiresDecision(CharacterBodyCardField.PubicHair)", BodySection, StringComparison.Ordinal);
    }

    /// <summary>
    /// The typical values are SUGGESTIONS drawn from the domain catalog, and the field still accepts anything typed:
    /// a list copied into the markup would drift from the catalog the template editor reads (B-122).
    /// </summary>
    [Fact]
    public void BodyCard_HairFieldsOfferTheCatalogsTypicalValues_WithoutBecomingAClosedList()
    {
        Assert.Contains("PhysicalAttributesCatalog.BodyHairOptions", BodySection, StringComparison.Ordinal);
        Assert.Contains("PhysicalAttributesCatalog.PubicHairOptions", BodySection, StringComparison.Ordinal);

        // A datalist suggests; a select would force one of the listed values.
        Assert.Contains("list=\"body-card-body-hair-options\"", BodySection, StringComparison.Ordinal);
        Assert.Contains("list=\"body-card-pubic-hair-options\"", BodySection, StringComparison.Ordinal);
        Assert.DoesNotContain("<select", Slice(BodySection, "CharacterBodyCardField.BodyHair", "CharacterBodyCardField.PubicHair"), StringComparison.Ordinal);
    }

    [Fact]
    public void BodyCard_IsSavedThroughTheService_UnderTheVersionTheEditorLoaded()
    {
        Assert.Contains("BodyService.SaveBodyCardAsync(_bodyCard, _bodyCardVersion)", Source, StringComparison.Ordinal);
        Assert.Contains("_bodyCardVersion = _bodyCard.Version;", Source, StringComparison.Ordinal);
        // B-127: identity is read through the RESOLVED character template, not the raw route id.
        Assert.Contains("BodyService.GetBodyCardAsync(IdentityKey)", Source, StringComparison.Ordinal);
        Assert.Contains("OwnerResolver.ResolveAsync(characterId)", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("BodyService.GetBodyCardAsync(characterId)", Source, StringComparison.Ordinal);

        // The readiness the user is deciding against is the card's own answer, not a UI guess.
        Assert.Contains("_bodyCard.UnresolvedFields", Source, StringComparison.Ordinal);
        Assert.Contains("BodyCardStatus", BodySection, StringComparison.Ordinal);
    }

    [Fact]
    public void BodySection_OffersNoBatchSweepOrGenerateAll()
    {
        // Asserted over the CONTROLS, not the prose: the section explains to the user that there is no batch
        // action, and a raw text search would trip on that sentence instead of on a real handler.
        var handlers = Regex.Matches(BodySection, "@onclick=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToList();
        Assert.NotEmpty(handlers);

        foreach (var forbidden in new[] { "all", "every", "sweep", "batch" })
        {
            Assert.DoesNotContain(handlers, handler =>
                handler.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string Slice(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{startMarker}' was not found in CharacterStudio.razor.");
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"'{endMarker}' was not found after '{startMarker}' in CharacterStudio.razor.");
        return text[start..end];
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "DreamGenClone.sln")))
                return current.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the DreamGenClone repository root.");
    }
}
