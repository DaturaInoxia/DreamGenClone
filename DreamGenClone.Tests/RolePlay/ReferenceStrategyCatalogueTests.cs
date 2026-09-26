using System.Text.RegularExpressions;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The reference-strategy catalogue exists because the same per-element strategy table had been written three times
/// and had drifted in three directions: the apply panel's <c>Location</c> arm listed only ControlNet, the step
/// composer's default arm silently handed <c>Location</c> and <c>Wardrobe</c> the face set, and the edit surface
/// hardcoded a two-entry list as if that were the whole world.
///
/// These tests hold both halves of that fix: the catalogue is total and single (unit tests), and no image-step
/// surface may carry its own copy again (the structural guard, which reads the Razor sources).
/// </summary>
public sealed class ReferenceStrategyCatalogueTests
{
    /// <summary>
    /// Every surface that presents an image step. A per-element strategy table in any of these is the defect this
    /// guard exists to catch, so the list is deliberately the surfaces and not the catalogue's consumers in general.
    /// </summary>
    private static readonly string[] ImageStepSurfacePaths =
    [
        Path.Combine("DreamGenClone.Web", "Components", "Shared", "ImageStepComposer.razor"),
        Path.Combine("DreamGenClone.Web", "Components", "Pages", "SceneImageStudio.razor"),
        Path.Combine("DreamGenClone.Web", "Components", "Pages", "CompositionComposer.razor"),
        Path.Combine("DreamGenClone.Web", "Components", "Pages", "SceneImageCompose.razor"),
        Path.Combine("DreamGenClone.Web", "Components", "Editing", "ImageEditWorkspace.razor"),
        Path.Combine("DreamGenClone.Web", "Components", "Editing", "LoraDatasetWorkspace.razor"),
        Path.Combine("DreamGenClone.Web", "Components", "Pages", "PoseLibraryPage.razor"),
        Path.Combine("DreamGenClone.Web", "Components", "Assets", "PromptAssetCreator.razor")
    ];

    /// <summary>
    /// Two adjacent strategy names is what makes a LIST - a single name can be a legitimate degrade to text, and a
    /// lone name beside a comma can be an argument to a comparison, both of which are fine. Comments are stripped
    /// first, so prose that quotes the old lists is not a violation.
    /// </summary>
    private static readonly Regex StrategyListEntry = new(
        "\"(?:TextOnly|ReferenceConditioning|NativeMultiReference|ControlNet|WardrobeTryOn|Lora)\""
        + "\\s*,\\s*\"(?:TextOnly|ReferenceConditioning|NativeMultiReference|ControlNet|WardrobeTryOn|Lora)\"",
        RegexOptions.Compiled);

    [Fact]
    public void EverySlotKind_HasACatalogueEntry_AndAlwaysOffersText()
    {
        foreach (var slotKind in Enum.GetValues<ImageStepSlotKind>())
        {
            var strategies = ReferenceStrategyCatalogue.ForSlotKind(slotKind);

            Assert.NotEmpty(strategies);
            Assert.Contains(ReferenceStrategyCatalogue.TextOnly, strategies);
        }
    }

    [Fact]
    public void EveryCatalogueEntry_IsAKnownStrategyName()
    {
        // A typo here is invisible at runtime: it simply never matches a model's QualifiedStrategies and the option
        // silently disappears from every menu. Naming the allowed set makes that a test failure instead.
        string[] known =
        [
            ReferenceStrategyCatalogue.TextOnly,
            ReferenceStrategyCatalogue.ReferenceConditioning,
            ReferenceStrategyCatalogue.NativeMultiReference,
            ReferenceStrategyCatalogue.ControlNet,
            ReferenceStrategyCatalogue.Lora,
            ReferenceStrategyCatalogue.WardrobeTryOn
        ];

        foreach (var slotKind in Enum.GetValues<ImageStepSlotKind>())
        {
            foreach (var strategy in ReferenceStrategyCatalogue.ForSlotKind(slotKind))
            {
                Assert.Contains(strategy, known);
            }
        }
    }

    [Fact]
    public void SlotKindCatalogue_AndElementKeyCatalogue_AreTheSameCatalogue()
    {
        // The step composer reads by slot kind and the apply panel reads by element key. If these ever disagree, the
        // same reference element offers different mechanisms depending on which surface the operator opened - the
        // drift this catalogue exists to end.
        foreach (var slotKind in Enum.GetValues<ImageStepSlotKind>())
        {
            var elementKey = ReferenceSlotPlanner.ElementKeyFor(slotKind);

            Assert.True(
                ReferenceStrategyCatalogue.TryForElementKey(elementKey, out var byElementKey),
                $"Slot kind '{slotKind}' maps to element key '{elementKey}', which the catalogue does not know.");
            Assert.Equal(ReferenceStrategyCatalogue.ForSlotKind(slotKind), byElementKey);
        }
    }

    [Fact]
    public void Intersect_KeepsMeaningOrder_AndDropsMechanismsTheModelCannotRun()
    {
        var meaningful = ReferenceStrategyCatalogue.ForSlotKind(ImageStepSlotKind.Face);

        var offered = ReferenceStrategyCatalogue.Intersect(
            meaningful,
            [ReferenceStrategyCatalogue.NativeMultiReference, ReferenceStrategyCatalogue.ReferenceConditioning]);

        // Meaning order (ReferenceConditioning before NativeMultiReference for a face), not capability order.
        Assert.Equal(
            [ReferenceStrategyCatalogue.ReferenceConditioning, ReferenceStrategyCatalogue.NativeMultiReference],
            offered);
    }

    [Fact]
    public void Intersect_WhenTheModelQualifiesForNothing_YieldsNothing_NotAGuessedSet()
    {
        var offered = ReferenceStrategyCatalogue.Intersect(
            ReferenceStrategyCatalogue.ForSlotKind(ImageStepSlotKind.Location),
            ["SomeFutureStrategy"]);

        Assert.Empty(offered);
    }

    [Fact]
    public void NoImageStepSurface_HardcodesAPerElementStrategyCatalogue()
    {
        var root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var relativePath in ImageStepSurfacePaths)
        {
            var fullPath = Path.Combine(root, relativePath);
            Assert.True(File.Exists(fullPath), $"Image-step surface not found: {relativePath}");

            var code = StripComments(File.ReadAllText(fullPath));
            var match = StrategyListEntry.Match(code);
            if (match.Success)
            {
                var line = code[..match.Index].Count(character => character == '\n') + 1;
                violations.Add($"{relativePath}:{line} contains a hardcoded strategy list ('{match.Value.Trim()}').");
            }
        }

        Assert.True(
            violations.Count == 0,
            "A per-element strategy catalogue must live only in ReferenceStrategyCatalogue. Violations:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static string StripComments(string razorSource)
    {
        var withoutRazorComments = Regex.Replace(razorSource, "@\\*.*?\\*@", string.Empty, RegexOptions.Singleline);
        var kept = withoutRazorComments
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));

        return string.Join('\n', kept);
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "DreamGenClone.sln"))
                && File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find the DreamGenClone repository root from '{AppContext.BaseDirectory}'.");
    }
}
