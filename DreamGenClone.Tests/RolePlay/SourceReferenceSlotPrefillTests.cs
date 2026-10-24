using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The edit step now OPENS on the references its source image was made with, so these tests hold the rules that decide
/// what may be re-bound and what must instead be refused by name.
///
/// <para>
/// The point of the feature is that the operator can SEE the references; the point of these rules is that a reference
/// the step cannot actually carry is never bound to look like it will be. Every refusal below is asserted to name what
/// was refused, because "one of your references was dropped" is not actionable.
/// </para>
/// </summary>
public sealed class SourceReferenceSlotPrefillTests
{
    private const string ModelId = "model-qwen-2.1";

    private static readonly ImageStepActor Becky = new("p-becky", "Becky");

    private static ImageStepBlueprint EditStep => ImageStepBlueprintFactory.ForEditElements([Becky]);

    [Fact]
    public async Task ARecordedLocationAndFace_AreSeededWithTheirRecordedOrder()
    {
        var seed = await SourceReferenceSlotPrefill.ResolveAsync(
            new StubStrategies(), ModelId, 10, EditStep,
            [FaceBinding(ordinal: 1), LocationBinding(ordinal: 2)]);

        Assert.Equal(SourceReferenceSeedOutcome.Seeded, seed.Outcome);
        Assert.Empty(seed.Refusals);
        Assert.Equal(2, seed.Bindings.Count);
        Assert.Equal(nameof(ImageStepSlotKind.Face), seed.Bindings[0].Kind);
        Assert.Equal(1, seed.Bindings[0].Ordinal);
        Assert.Equal(nameof(ImageStepSlotKind.Location), seed.Bindings[1].Kind);
        Assert.Equal(2, seed.Bindings[1].Ordinal);
    }

    /// <summary>
    /// A binding whose slot this step does not declare is refused BY NAME. The composer renders no control for it, so
    /// binding it would send the model a reference with nothing on screen saying so.
    /// </summary>
    [Fact]
    public async Task ABindingForAnActorTheStepDoesNotAddress_IsRefusedByName()
    {
        var seed = await SourceReferenceSlotPrefill.ResolveAsync(
            new StubStrategies(), ModelId, 10, EditStep,
            [FaceBinding(ordinal: 1, actorKey: "p-someone-else")]);

        Assert.False(seed.IsSeeded);
        var refusal = Assert.Single(seed.Refusals);
        Assert.Contains("no 'Face' slot for actor 'p-someone-else'", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scratch image has no implementable route on any surface, and the refusal is the RESOLVER's own words rather
    /// than a second copy of the rule that could drift from it.
    /// </summary>
    [Fact]
    public async Task AScratchImageBinding_IsRefusedInTheResolversOwnWords()
    {
        var scratch = LocationBinding(ordinal: 1);
        scratch.Source = nameof(ImageStepReferenceSourceKind.ScratchImage);

        var seed = await SourceReferenceSlotPrefill.ResolveAsync(
            new StubStrategies(), ModelId, 10, EditStep, [scratch]);

        Assert.False(seed.IsSeeded);
        var refusal = Assert.Single(seed.Refusals);
        Assert.Contains("scratch image", refusal, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A strategy the chosen model cannot carry is refused with the strategy resolver's own reason.</summary>
    [Fact]
    public async Task AStrategyTheSelectedModelCannotCarry_IsRefusedWithItsReason()
    {
        var binding = LocationBinding(ordinal: 1);
        binding.Strategy = ReferenceStrategyCatalogue.ControlNet;

        var seed = await SourceReferenceSlotPrefill.ResolveAsync(
            new StubStrategies { Unavailable = ReferenceStrategyCatalogue.ControlNet }, ModelId, 10, EditStep, [binding]);

        Assert.False(seed.IsSeeded);
        var refusal = Assert.Single(seed.Refusals);
        Assert.Contains("'ControlNet' is not qualified", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A seed that would not fit the model's own reference budget is declined WHOLE, with the reason. Nothing is
    /// trimmed to fit: a reference the operator cannot see is one they cannot clear, and seeding past the budget would
    /// leave the step holding a binding the planner refuses the moment any control is touched.
    /// </summary>
    [Fact]
    public async Task ASeedPastTheModelsReferenceBudget_IsDeclinedWhole()
    {
        var recorded = Enumerable.Range(1, 7)
            .Select(index => LocationBinding(ordinal: index))
            .ToList();

        var seed = await SourceReferenceSlotPrefill.ResolveAsync(
            new StubStrategies(), ModelId, maxReferences: 6, EditStep, recorded);

        Assert.False(seed.IsSeeded);
        var refusal = Assert.Single(seed.Refusals);
        Assert.Contains("made with 7 references", refusal, StringComparison.Ordinal);
        Assert.Contains("only 6 are usable", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model that declares no reference capacity at all is named by the missing setting, rather than being handed a
    /// seed it will throw on. This is the same answer the composer gives when a step offers slots on such a model.
    /// </summary>
    [Fact]
    public async Task AModelWithNoReferenceCapacity_IsRefusedNamingTheSetting()
    {
        var seed = await SourceReferenceSlotPrefill.ResolveAsync(
            new StubStrategies(), ModelId, maxReferences: 0, EditStep, [LocationBinding(ordinal: 1)]);

        Assert.False(seed.IsSeeded);
        var refusal = Assert.Single(seed.Refusals);
        Assert.Contains("MaxReferences", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// "This image was made from nothing" and "there is something to seed but no model is chosen yet" are different
    /// facts, and the second is not a refusal — it is a question to ask again once a model is picked.
    /// </summary>
    [Fact]
    public async Task NoRecordedReferences_AndNoModelYet_AreDistinctOutcomes()
    {
        var nothing = await SourceReferenceSlotPrefill.ResolveAsync(
            new StubStrategies(), ModelId, 10, EditStep, []);
        var noModel = await SourceReferenceSlotPrefill.ResolveAsync(
            new StubStrategies(), registeredModelId: null, 10, EditStep, [LocationBinding(ordinal: 1)]);

        Assert.Equal(SourceReferenceSeedOutcome.NothingRecorded, nothing.Outcome);
        Assert.Empty(nothing.Refusals);
        Assert.Equal(string.Empty, SourceReferenceSlotPrefill.Notice(nothing));

        Assert.Equal(SourceReferenceSeedOutcome.NoModelSelected, noModel.Outcome);
        Assert.Empty(noModel.Refusals);
        Assert.Contains("Choose an image model", SourceReferenceSlotPrefill.Notice(noModel), StringComparison.Ordinal);
    }

    /// <summary>
    /// A text-only binding carries no image, so there is nothing to re-bind and nothing to report. Counting it as a
    /// refusal would put a notice on screen for an element the operator never bound an image to.
    /// </summary>
    [Fact]
    public async Task ATextOnlyBinding_IsNeitherSeededNorRefused()
    {
        var textOnly = LocationBinding(ordinal: 1);
        textOnly.Strategy = ReferenceStrategyCatalogue.TextOnly;

        var seed = await SourceReferenceSlotPrefill.ResolveAsync(
            new StubStrategies(), ModelId, 10, EditStep, [textOnly]);

        Assert.Equal(SourceReferenceSeedOutcome.NothingRecorded, seed.Outcome);
        Assert.Empty(seed.Refusals);
    }

    /// <summary>
    /// The actors a recorded reference names have to reach the CAST: a binding whose actor the step does not declare
    /// has no slot, and the planner refuses it — so omitting them would arm a failure for the next control the
    /// operator touched.
    /// </summary>
    [Fact]
    public void ActorKeysOf_ReturnsEachRecordedActorOnce()
    {
        var keys = SourceReferenceSlotPrefill.ActorKeysOf(
            [FaceBinding(ordinal: 1), FaceBinding(ordinal: 2, actorKey: "p-dean"), LocationBinding(ordinal: 3)]);

        Assert.Equal(new[] { "p-becky", "p-dean" }, keys);
    }

    /// <summary>The notice says what was bound, and repeats each refusal in the owner's own words.</summary>
    [Fact]
    public async Task TheNotice_SaysWhatWasBoundAndWhatWasNot()
    {
        var mixed = await SourceReferenceSlotPrefill.ResolveAsync(
            new StubStrategies(), ModelId, 10, EditStep,
            [LocationBinding(ordinal: 1), FaceBinding(ordinal: 2, actorKey: "p-someone-else")]);

        var notice = SourceReferenceSlotPrefill.Notice(mixed);

        Assert.Contains("Bound the 1 reference(s)", notice, StringComparison.Ordinal);
        Assert.Contains("p-someone-else", notice, StringComparison.Ordinal);
    }

    /// <summary>
    /// The step the prefill seeds from must be the step the workspace RENDERS, or a slot the blueprint declares and
    /// the prefill does not know about would be silently unbindable. Pinned against the factory the workspace uses.
    /// </summary>
    [Fact]
    public void TheEditStep_DeclaresTheSlotsTheRecordCanCarryBack()
    {
        var kinds = EditStep.Slots.Select(slot => slot.SlotKind).ToList();

        Assert.Contains(ImageStepSlotKind.Face, kinds);
        Assert.Contains(ImageStepSlotKind.Body, kinds);
        Assert.Contains(ImageStepSlotKind.Wardrobe, kinds);
        Assert.Contains(ImageStepSlotKind.Location, kinds);
        Assert.Contains(ImageStepSlotKind.Pose, kinds);
    }

    /// <summary>
    /// A second image on a slot that holds one is refused by name, because the planner would refuse it the moment the
    /// operator touched any control — an image made with two faces for one character is the case, and it is not a
    /// reason to hand them a broken step.
    /// </summary>
    [Fact]
    public async Task TwoImagesForASlotThatHoldsOne_AreRefusedByName()
    {
        var seed = await SourceReferenceSlotPrefill.ResolveAsync(
            new StubStrategies(), ModelId, 10, EditStep,
            [FaceBinding(ordinal: 1), FaceBinding(ordinal: 2)]);

        Assert.Single(seed.Bindings);
        var refusal = Assert.Single(seed.Refusals);
        Assert.Contains("holds one image", refusal, StringComparison.Ordinal);
    }

    /// <summary>…while a multi slot keeps all of them: a wardrobe is a look, and a location is several accepted views
    /// of one place.</summary>
    [Fact]
    public async Task SeveralImagesForAMultiSlot_AreAllSeeded()
    {
        var seed = await SourceReferenceSlotPrefill.ResolveAsync(
            new StubStrategies(), ModelId, 10, EditStep,
            [LocationBinding(ordinal: 1), LocationBinding(ordinal: 2)]);

        Assert.Equal(2, seed.Bindings.Count);
        Assert.Empty(seed.Refusals);
    }

    private static ReferenceApplicationSelection FaceBinding(int ordinal, string actorKey = "p-becky") => new()
    {
        ElementKey = "Identity",
        Kind = nameof(ImageStepSlotKind.Face),
        SemanticRole = "character identity",
        ActorKey = actorKey,
        Source = nameof(ImageStepReferenceSourceKind.IdentityPackAsset),
        Strategy = ReferenceStrategyCatalogue.NativeMultiReference,
        Ordinal = ordinal,
        IdentityPackId = "pack-1",
        ReferenceAssetId = "face-1",
        ReferenceLabel = "Front"
    };

    private static ReferenceApplicationSelection LocationBinding(int ordinal) => new()
    {
        ElementKey = "Location",
        Kind = nameof(ImageStepSlotKind.Location),
        SemanticRole = "location continuity",
        Source = nameof(ImageStepReferenceSourceKind.ApprovedSceneAsset),
        Strategy = ReferenceStrategyCatalogue.NativeMultiReference,
        Ordinal = ordinal,
        SceneAssetId = "location-asset-1",
        SceneAssetImageId = "location-image-1",
        SceneAssetVersion = 1,
        SceneAssetSha256 = "ABC123",
        ReferenceLabel = "Indoor Front"
    };

    /// <summary>Every strategy is qualified unless the test names one that is not, so a test only states what it is
    /// actually about.</summary>
    private sealed class StubStrategies : IReferenceStrategyResolver
    {
        public string? Unavailable { get; init; }

        public Task<ReferenceStrategyResolution> ResolveAsync(
            string modelId, string strategy, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Equals(strategy, Unavailable, StringComparison.OrdinalIgnoreCase)
                ? new ReferenceStrategyResolution(
                    ReferenceStrategyResolutionStatus.Unqualified,
                    strategy,
                    $"'{strategy}' is not qualified for model '{modelId}'.")
                : new ReferenceStrategyResolution(
                    ReferenceStrategyResolutionStatus.Possible,
                    strategy,
                    $"'{strategy}' is qualified for model '{modelId}'."));
    }
}
