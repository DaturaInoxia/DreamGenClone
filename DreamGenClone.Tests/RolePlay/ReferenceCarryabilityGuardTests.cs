using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// A binding-derived removal is only legitimate for a reference the render will actually SEND, so the resolved image
/// model has to be able to carry every binding the removal is derived from.
///
/// <para>
/// This is the difference between a reference-native model and a text-only one, and it is the whole reason a Krea 2 or
/// BigLust prompt must keep the complete character description: with no reference in the call, nothing takes the
/// appearance over, and removing the prose would leave the render with no identity at all. Every surface already
/// DERIVES no removal for a binding that supplies no image (see
/// <see cref="ReferenceBindingPromptRemoval.ImpliesRemoval"/>); this guard is what stops the other direction — a
/// binding that claims to supply one when the resolved model cannot carry it.
/// </para>
/// </summary>
public sealed class ReferenceCarryabilityGuardTests
{
    private static ResolvedImageModel Model(string identifier) => new(
        "http://localhost",
        "/prompt",
        300,
        null,
        identifier,
        ImageContentPolicy.AdultAllowed,
        "Local",
        false,
        SceneImageModelFamily.QwenImage21,
        SceneImagePromptDialect.NaturalLanguage,
        ImageProtocol.ComfyUi);

    private static ReferenceApplicationSelection Binding(
        string elementKey,
        string strategy,
        string? actorKey = null) => new()
    {
        ElementKey = elementKey,
        Kind = actorKey is null ? nameof(ImageStepSlotKind.Location) : nameof(ImageStepSlotKind.Face),
        ActorKey = actorKey,
        SemanticRole = "role",
        Strategy = strategy,
        SceneAssetId = "asset-1",
        SceneAssetImageId = "image-1",
        SceneAssetVersion = 1,
        SceneAssetSha256 = "SHA"
    };

    [Fact]
    public void ANativeReferenceBindingOnACapableModel_IsAccepted()
    {
        SceneImagePromptGenerationJobHandler.EnsureReferencesAreCarryable(
            [Binding("Body", ReferenceStrategyCatalogue.NativeMultiReference, actorKey: "p0")],
            Model("qwen_image_2.1_int8_convrot.safetensors"));
    }

    /// <summary>
    /// The IP-Adapter/PuLID identity MECHANISM is a render concept that the generation surface does not implement as a
    /// carried reference image, so a binding claiming it is refused by name — never dropped to make the prompt smaller.
    /// </summary>
    [Fact]
    public void ABindingTheGenerationSurfaceDoesNotImplement_IsRefusedByName()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => SceneImagePromptGenerationJobHandler.EnsureReferencesAreCarryable(
                [Binding("Body", ReferenceStrategyCatalogue.ReferenceConditioning, actorKey: "p0")],
                Model("krea2_turbo.safetensors")));

        Assert.Contains("ReferenceConditioning", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Body'", error.Message, StringComparison.Ordinal);
        Assert.Contains("krea2_turbo.safetensors", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The text-only case, which is the one that must NOT refuse: a binding whose strategy carries the element by
    /// prose derives no removal, so it is left alone and the character's full description survives into the prompt.
    /// </summary>
    [Fact]
    public void ATextOnlyBinding_IsLeftAloneSoTheDescriptionSurvives()
    {
        SceneImagePromptGenerationJobHandler.EnsureReferencesAreCarryable(
            [Binding("Body", ReferenceStrategyCatalogue.TextOnly, actorKey: "p0")],
            Model("krea2_turbo.safetensors"));

        Assert.Empty(ReferenceBindingPromptRemoval.Derive(
            [Binding("Body", ReferenceStrategyCatalogue.TextOnly, actorKey: "p0")]));
    }

    [Fact]
    public void NoBindings_IsNothingToRefuse()
    {
        SceneImagePromptGenerationJobHandler.EnsureReferencesAreCarryable(null, Model("krea2_turbo.safetensors"));
        SceneImagePromptGenerationJobHandler.EnsureReferencesAreCarryable([], Model("krea2_turbo.safetensors"));
    }

    /// <summary>
    /// A source-IMAGE transform pass applies an authored, hash-verified instruction to the image it was queued on, and
    /// that image already holds the place and the identity it was rendered with. Inheriting the source's bindings made
    /// the pass re-resolve ANOTHER render's references against its own editor model — and the edit models declare no
    /// reference strategy at all — so a camera angle on an image rendered with a Location reference failed before it
    /// could render (reported live 2026-10-13; <c>media-edit-image-editing:a8114ced…</c>).
    ///
    /// <para>
    /// Pinned as a source contract because re-introducing the copy fails nothing else: the pass would simply carry
    /// another render's bindings again.
    /// </para>
    /// </summary>
    [Fact]
    public void SourceImageTransformPasses_DoNotInheritTheSourcesReferences()
    {
        var service = ReadWebFile("Application", "RolePlay", "SceneImageService.cs");

        Assert.DoesNotContain(
            "AppliedReferenceBindingsJson = source.AppliedReferenceBindingsJson", service, StringComparison.Ordinal);
    }

    /// <summary>
    /// The enqueue-time proof asks the EDIT resolver's own predicate which bindings travel as a reference IMAGE, rather
    /// than restating the rule: a second copy would drift from the one <c>ResolveAsync</c> selects by, and the drift
    /// would show up as a run refused for a binding the job would have accepted (or the reverse).
    /// </summary>
    [Fact]
    public void TheEnqueueProof_UsesTheResolversOwnPredicate()
    {
        var service = ReadWebFile("Application", "RolePlay", "SceneImageService.cs");
        var resolver = ReadWebFile("Application", "RolePlay", "Editing", "MediaEditReferenceResolver.cs");

        Assert.Contains("MediaEditReferenceResolver.CarriesReferenceImage(", service, StringComparison.Ordinal);
        Assert.Contains(
            "public static bool CarriesReferenceImage(", resolver, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Composition page asks the seed OWNER, which answers with the render model's capability, instead of building
    /// the binding itself — building it itself is how a <c>NativeMultiReference</c> binding reached a Krea 2 render
    /// that declares <c>["TextOnly","Lora"]</c> (2026-10-13).
    /// </summary>
    [Fact]
    public void TheCompositionPage_AsksTheSeedOwnerInsteadOfBuildingTheBinding()
    {
        var page = ReadWebFile("Components", "Pages", "CompositionComposer.razor");

        Assert.Contains("LocationBackdropSlotPrefill.ResolveAsync(", page, StringComparison.Ordinal);
        Assert.DoesNotContain("LocationBackdropSlotPrefill.For(", page, StringComparison.Ordinal);
    }

    private static string ReadWebFile(params string[] relativeParts) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), "DreamGenClone.Web", .. relativeParts]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root (holding DreamGenClone.sln) was not found.");
    }
}
