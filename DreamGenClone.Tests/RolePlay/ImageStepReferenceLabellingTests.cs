using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The reference-binding surfaces show the operator a NAME, not an id.
///
/// This is the defect this file guards (reported 2026-10-01): the step composer rendered
/// <c>Body: de351eb3-69d3-421a-a762-79ae8ee183ed</c> and
/// <c>Body for de351eb3-…: pack asset 172061ca…</c>. Both names existed all along — the character's on
/// <see cref="ImageStepActor.DisplayName"/>, the image's on the pack picker's selection — and were dropped on the way
/// to the screen: the factory copied only the actor KEY onto the slot blueprint, and the binding stored only the two
/// ids. These tests hold the data path that carries them.
/// </summary>
public sealed class ImageStepReferenceLabellingTests
{
    private const string BeckyKey = "profile-key-becky";
    private static readonly ImageStepActor Becky = new(BeckyKey, "Becky");

    [Fact]
    public void EverySlotNamingACharacterAlsoCarriesThatCharactersName()
    {
        var blueprints = new[]
        {
            ImageStepBlueprintFactory.ForProductionStudio([Becky]),
            ImageStepBlueprintFactory.ForEdit([Becky]),
            ImageStepBlueprintFactory.ForLoraCell(Becky),
            ImageStepBlueprintFactory.ForAssetCreate(Becky),
            ImageStepBlueprintFactory.ForPackIdentityComposition([Becky]),
            ImageStepBlueprintFactory.ForEditElements([Becky]),
            ImageStepBlueprintFactory.ForCharacterPoseLibrary(Becky),
            ImageStepBlueprintFactory.ForPoseLibraryTest(Becky)
        };

        foreach (var blueprint in blueprints)
        {
            var namingACharacter = blueprint.Slots
                .Where(slot => !string.IsNullOrWhiteSpace(slot.ActorKey))
                .ToArray();

            // Every surface that names a character names at least one slot, and each of those carries the name the
            // host handed over - so a slot can no longer reach the screen as a bare profile key.
            Assert.True(
                namingACharacter.Length > 0,
                $"A blueprint names a character but no slot carries one: {string.Join(", ", blueprint.Slots.Select(slot => slot.SlotKind))}");

            Assert.All(namingACharacter, slot => Assert.Equal(
                Becky.DisplayName,
                slot.ActorDisplayName));
        }
    }

    [Fact]
    public void APlannedBindingKeepsTheImageLabelThePickerResolved()
    {
        // ForAssetCreate declares its character slots as optional, so one assignment is a complete step; its sources
        // include the identity pack, which is where the label comes from.
        var blueprint = ImageStepBlueprintFactory.ForAssetCreate(Becky);

        var assigned = new ImageStepSlotAssignment(
            ImageStepSlotKind.Face,
            Becky.ActorKey,
            new ImageStepSlotSource(
                ImageStepReferenceSourceKind.IdentityPackAsset,
                ReferenceStrategyResolver.IdentityNativeMultiReference,
                IdentityPackId: "pack-1",
                ReferenceAssetId: "asset-1",
                ReferenceLabel: "Front"));

        var planned = ReferenceSlotPlanner.Plan(blueprint, [assigned], 6);

        // The label travels WITH the ids rather than being looked up later, because the component that displays the
        // binding holds no services: it is host-driven, so anything it shows has to arrive as data.
        var binding = Assert.Single(planned);
        Assert.Equal("asset-1", binding.ReferenceAssetId);
        Assert.Equal("Front", binding.ReferenceLabel);
    }

    [Fact]
    public void TheStalenessLineNamesTheImageWhenTheBindingKnowsItsName()
    {
        // The strategy moves as well, because a change line is only produced when something the step really depends on
        // moved - the label is display data and deliberately does NOT make a generated prompt look stale (below). What
        // is asserted here is that when a line IS produced, it names the image instead of showing its id.
        var previous = new[] { FaceBinding("Front", ReferenceStrategyResolver.IdentityNativeMultiReference) };
        var current = new[] { FaceBinding("Front · Clothed", ReferenceStrategyResolver.IdentityNativeMultiReference) };
        current[0].SceneAssetSha256 = "a-different-image";

        var changes = StepPromptStaleness.DescribeChanges(previous, current);

        var line = Assert.Single(changes);
        Assert.Contains("Front · Clothed", line, StringComparison.Ordinal);
        Assert.DoesNotContain("asset-1", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ALabelledBindingDoesNotMakeAGeneratedPromptLookStale()
    {
        // The label is DISPLAY data. It must not enter the signature a generated prompt was recorded against, or every
        // step bound before labels existed would report a changed reference the operator never touched.
        var unlabelled = new[] { FaceBinding(null, ReferenceStrategyResolver.IdentityNativeMultiReference) };
        var labelled = new[] { FaceBinding("Front · Clothed", ReferenceStrategyResolver.IdentityNativeMultiReference) };

        Assert.Equal(
            StepPromptStaleness.SignatureFor(unlabelled),
            StepPromptStaleness.SignatureFor(labelled));
    }

    private static ReferenceApplicationSelection FaceBinding(string? referenceLabel, string strategy) => new()
    {
        ElementKey = "Identity",
        Kind = ImageStepSlotKind.Face.ToString(),
        ActorKey = BeckyKey,
        Source = ImageStepReferenceSourceKind.IdentityPackAsset.ToString(),
        Strategy = strategy,
        Ordinal = 1,
        IdentityPackId = "pack-1",
        ReferenceAssetId = "asset-1",
        ReferenceLabel = referenceLabel
    };
}
