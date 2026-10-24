using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The "use as reference" image-card command names ONE place: the image it was pressed on. Its reload brings back what
/// made that image — which includes the place that image was rendered in — and the location slot is the multi-valued
/// one (several views of one place are several references), so applying the request on top of the reload used to leave
/// the step anchored to the old place AND the picked image.
///
/// <para>
/// Measured 2026-10-19 on the operator's own production group. The rule these tests pin: a request binds as the slot's
/// SOLE reference, while a second view of the same place stays an operator action in the picker.
/// </para>
/// </summary>
public sealed class UseAsReferenceSoleLocationTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string ComposerSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Shared", "ImageStepComposer.razor"));

    /// <summary>The Composition step's own blueprint — the surface the command hands off to.</summary>
    private static ImageStepBlueprint Blueprint()
        => ImageStepBlueprintFactory.ForPackIdentityComposition([new ImageStepActor("becky", "Becky")]);

    [Fact]
    public void UseAsReference_BindsTheSelectedImageAsTheOnlyLocationReference()
    {
        var blueprint = Blueprint();
        var location = Assert.Single(blueprint.Slots, slot => slot.SlotKind == ImageStepSlotKind.Location);

        // Exactly what the reload puts back before the request is applied: the build and the place the source image
        // was rendered in.
        ImageStepSlotAssignment[] restored =
        [
            new(ImageStepSlotKind.Body, "becky", PackImage("pack-asset-1")),
            new(ImageStepSlotKind.Location, null, AppImage("rendered-old", "SHA-old"))
        ];

        var planned = ReferenceSlotPlanner.Plan(
            blueprint,
            ReferenceSlotPlanner.AssignmentsFor(
                restored, location, AppImage("rendered-picked", "SHA-picked"), asSoleReference: true),
            maxReferences: 10);

        var bound = Assert.Single(planned, binding => binding.Kind == nameof(ImageStepSlotKind.Location));
        Assert.Equal("rendered-picked", bound.ReferenceImageId);
        Assert.Equal("SHA-picked", bound.ReferenceImageSha256);

        // The reload half of the command still works: what made the image comes back.
        Assert.Contains(planned, binding => binding.Kind == nameof(ImageStepSlotKind.Body));
    }

    /// <summary>
    /// The capability the slot declares is untouched: an operator binding a second view of the same place still gets two
    /// references. Only the command that names one reference replaces.
    /// </summary>
    [Fact]
    public void AddingASecondViewOfThePlace_StillAddsToTheSlot()
    {
        var blueprint = Blueprint();
        var location = Assert.Single(blueprint.Slots, slot => slot.SlotKind == ImageStepSlotKind.Location);

        ImageStepSlotAssignment[] restored =
        [
            new(ImageStepSlotKind.Location, null, AppImage("rendered-old", "SHA-old"))
        ];

        var planned = ReferenceSlotPlanner.Plan(
            blueprint,
            ReferenceSlotPlanner.AssignmentsFor(restored, location, AppImage("rendered-second-view", "SHA-second")),
            maxReferences: 10);

        Assert.Equal(2, planned.Count(binding => binding.Kind == nameof(ImageStepSlotKind.Location)));
        Assert.Contains(planned, binding => binding.ReferenceImageId == "rendered-old");
        Assert.Contains(planned, binding => binding.ReferenceImageId == "rendered-second-view");
    }

    /// <summary>A slot that holds one reference keeps replacing without the flag — the picker never stacks there.</summary>
    [Fact]
    public void ASingleReferenceSlot_ReplacesWithoutTheFlag()
    {
        var blueprint = Blueprint();
        var face = Assert.Single(blueprint.Slots, slot => slot.SlotKind == ImageStepSlotKind.Face);

        ImageStepSlotAssignment[] existing =
        [
            new(ImageStepSlotKind.Face, "becky", PackImage("pack-asset-old"))
        ];

        var planned = ReferenceSlotPlanner.Plan(
            blueprint,
            ReferenceSlotPlanner.AssignmentsFor(existing, face, PackImage("pack-asset-new")),
            maxReferences: 10);

        var bound = Assert.Single(planned);
        Assert.Equal(nameof(ImageStepSlotKind.Face), bound.Kind);
        Assert.Equal("pack-asset-new", bound.ReferenceAssetId);
    }

    /// <summary>
    /// The composer applies a host's request as the sole reference, and decides replace-vs-add in the planner rather
    /// than re-implementing it: a second copy of that rule is how the two surfaces would drift apart.
    /// </summary>
    [Fact]
    public void Composer_AppliesTheHostRequestAsTheSoleReference()
    {
        Assert.Contains(
            "ReferenceSlotPlanner.AssignmentsFor(Bindings.Select(ToAssignment), slot, source, asSoleReference)",
            ComposerSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "await BindSourceAsync(slot, request.ToSlotSource(StrategyFor(slot, request.SourceKind)), asSoleReference: true);",
            ComposerSource,
            StringComparison.Ordinal);

        // Nothing else may bind as the sole reference: the pickers keep the slot's own multi behaviour.
        Assert.Single(Occurrences(ComposerSource, "asSoleReference: true"));
    }

    private static IEnumerable<int> Occurrences(string source, string value)
    {
        for (var index = source.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            yield return index;
        }
    }

    private static ImageStepSlotSource AppImage(string imageId, string sha256) => new(
        SourceKind: ImageStepReferenceSourceKind.AppImage,
        Strategy: ReferenceStrategyResolver.IdentityNativeMultiReference,
        ReferenceImageId: imageId,
        ReferenceImageSha256: sha256,
        ReferenceLabel: imageId);

    private static ImageStepSlotSource PackImage(string referenceAssetId) => new(
        SourceKind: ImageStepReferenceSourceKind.IdentityPackAsset,
        Strategy: ReferenceStrategyResolver.IdentityNativeMultiReference,
        IdentityPackId: "pack-1",
        ReferenceAssetId: referenceAssetId,
        ReferenceLabel: referenceAssetId);

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
