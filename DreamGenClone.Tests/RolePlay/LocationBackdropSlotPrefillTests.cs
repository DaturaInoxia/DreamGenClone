using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

public sealed class LocationBackdropSlotPrefillTests
{
    [Fact]
    public void For_FillsTheDeclaredLocationSlot()
    {
        var blueprint = ImageStepBlueprintFactory.ForPackIdentityComposition([]);
        var backdrop = SceneImageLocationBackdrop.Create("loc-a", "img-a", "ABC", 2, "Front");

        var binding = LocationBackdropSlotPrefill.For(blueprint, backdrop);

        Assert.Equal("Location", binding.ElementKey);
        Assert.Equal(ImageStepSlotKind.Location.ToString(), binding.Kind);
        Assert.Equal("loc-a", binding.SceneAssetId);
        Assert.Equal("img-a", binding.SceneAssetImageId);
        Assert.Equal("ABC", binding.SceneAssetSha256);
        Assert.Equal(2, binding.SceneAssetVersion);
    }

    /// <summary>
    /// The seeded binding carries the operator's NAME for the image, exactly as a hand-picked one does.
    ///
    /// <para>
    /// Reported 2026-10-15: opening the Composition from the Production Studio showed <c>Location:
    /// c54505b624f749109b8139400f295609</c> while picking the same image by hand showed <c>Location:
    /// Couple_Other_Front_Close</c>. This was the ONLY prefill that dropped the label — <see
    /// cref="IdentityPackSlotPrefill"/> and the edit path both carry one — even though
    /// <see cref="SceneImageLocationBackdrop"/> REQUIRES it. Dropping it degraded the binding twice over: the composer
    /// fell back to the id (its last resort) and the prompt block fell back to the generic role
    /// "location continuity", so the same image was described differently depending on which path bound it.
    /// </para>
    /// </summary>
    [Fact]
    public void For_CarriesTheOperatorsNameForTheImage_SoTheSeedCannotDegradeToAnId()
    {
        var blueprint = ImageStepBlueprintFactory.ForPackIdentityComposition([]);
        var backdrop = SceneImageLocationBackdrop.Create("loc-a", "img-a", "ABC", 2, "Couple_Other_Front_Close");

        var binding = LocationBackdropSlotPrefill.For(blueprint, backdrop);

        Assert.Equal("Couple_Other_Front_Close", binding.ReferenceLabel);

        // The ID must never be what a surface shows while a name exists — the reason the label is carried at all.
        Assert.NotEqual(binding.SceneAssetImageId, binding.ReferenceLabel);
    }

    /// <summary>
    /// ...and the prompt names it too. A seeded reference used to be labelled with its generic ROLE
    /// ("location continuity") while a picked one was labelled with the image's name, because
    /// <c>ReferenceRoleClauses.LabelFor</c> falls back to <c>SemanticRole</c> when no label travelled.
    /// </summary>
    [Fact]
    public void For_LabelsThePromptBlockWithTheName_NotTheGenericRole()
    {
        var blueprint = ImageStepBlueprintFactory.ForPackIdentityComposition([]);
        var backdrop = SceneImageLocationBackdrop.Create("loc-a", "img-a", "ABC", 2, "Couple_Other_Front_Close");
        var binding = LocationBackdropSlotPrefill.For(blueprint, backdrop);

        // The ONE label owner decides this, so asking it is asking what the prompt will say.
        Assert.Equal("Couple_Other_Front_Close", ReferenceRoleClauses.LabelFor(binding));

        // A block is only emitted for two or more references (a single image is never tagged, B-160 follow-up), so the
        // line is read from a block that actually exists.
        var block = ReferenceRoleClauses.BlockFor(
        [
            .. ReferenceRoleClauses.RolesFor([binding]),
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Face, "Front")
        ]);

        Assert.Contains("(Couple_Other_Front_Close)", block, StringComparison.Ordinal);
        Assert.DoesNotContain("(location continuity)", block, StringComparison.Ordinal);
    }

    [Fact]
    public void For_RefusesABlueprintWithoutALocationSlot()
    {
        // An edit step declares a face slot per actor and no location slot.
        var blueprint = ImageStepBlueprintFactory.ForEdit([new ImageStepActor("tpl-dean", "Dean")]);

        var backdrop = SceneImageLocationBackdrop.Create("loc-a", "img-a", "ABC", 2, "Front");

        var exception = Assert.Throws<InvalidOperationException>(
            () => LocationBackdropSlotPrefill.For(blueprint, backdrop));
        Assert.Contains("Location", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HasLocationSlot_ReportsTheDeclaredSlot()
    {
        Assert.True(LocationBackdropSlotPrefill.HasLocationSlot(ImageStepBlueprintFactory.ForPackIdentityComposition([])));
        Assert.False(LocationBackdropSlotPrefill.HasLocationSlot(
            ImageStepBlueprintFactory.ForEdit([new ImageStepActor("tpl-dean", "Dean")])));
    }

    /// <summary>
    /// Measured 2026-10-13: the Composition blueprint declares its Location slot for EVERY model, so the declared slot
    /// was the only guard there was — and a Krea 2 render, which declares <c>["TextOnly","Lora"]</c> and cannot carry a
    /// reference image at all, was handed a <c>NativeMultiReference</c> binding and died in the render with "does not
    /// declare support for 'NativeMultiReference' in Model Manager". The capability is the MODEL's, so the seed asks
    /// the model before it builds anything.
    /// </summary>
    [Fact]
    public async Task Resolve_OnAModelThatCannotCarryAReference_SeedsNothingAndCarriesTheResolversReason()
    {
        var blueprint = ImageStepBlueprintFactory.ForPackIdentityComposition([]);
        var backdrop = SceneImageLocationBackdrop.Create("loc-a", "img-a", "ABC", 2, "Front");
        var strategies = new IdentityStrategyDecisionTests.MapReferenceStrategies
        {
            Statuses = { [ReferenceStrategyCatalogue.NativeMultiReference] = ReferenceStrategyResolutionStatus.Unqualified }
        };

        var seed = await LocationBackdropSlotPrefill.ResolveAsync(strategies, "krea-2", blueprint, backdrop);

        Assert.False(seed.IsSeeded);
        Assert.Null(seed.Binding);
        Assert.Equal(LocationBackdropSeedOutcome.ModelCannotCarry, seed.Outcome);
        // The operator reads the resolver's own diagnostic, so the notice and the render's refusal cannot word the same
        // fact two different ways.
        Assert.Contains("krea-2", seed.Reason, StringComparison.Ordinal);
        Assert.Contains(ReferenceStrategyCatalogue.NativeMultiReference, seed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_OnAModelThatCarriesAReference_SeedsTheDeclaredSlot()
    {
        var blueprint = ImageStepBlueprintFactory.ForPackIdentityComposition([]);
        var backdrop = SceneImageLocationBackdrop.Create("loc-a", "img-a", "ABC", 2, "Front");
        var strategies = new IdentityStrategyDecisionTests.MapReferenceStrategies
        {
            Statuses = { [ReferenceStrategyCatalogue.NativeMultiReference] = ReferenceStrategyResolutionStatus.Possible }
        };

        var seed = await LocationBackdropSlotPrefill.ResolveAsync(strategies, "qwen-image-2.1", blueprint, backdrop);

        Assert.True(seed.IsSeeded);
        var binding = Assert.IsType<ReferenceApplicationSelection>(seed.Binding);
        Assert.Equal("Location", binding.ElementKey);
        Assert.Equal("loc-a", binding.SceneAssetId);
        Assert.Equal("img-a", binding.SceneAssetImageId);
    }

    /// <summary>
    /// A blueprint that names no Location slot is still refused by name rather than having the binding quietly
    /// dropped: the seed is what makes the render the place, so a surface that cannot show one has to say so.
    /// </summary>
    [Fact]
    public async Task Resolve_OnABlueprintWithoutALocationSlot_RefusesRatherThanDroppingTheBinding()
    {
        var blueprint = ImageStepBlueprintFactory.ForEdit([new ImageStepActor("tpl-dean", "Dean")]);
        var backdrop = SceneImageLocationBackdrop.Create("loc-a", "img-a", "ABC", 2, "Front");
        var strategies = new IdentityStrategyDecisionTests.MapReferenceStrategies
        {
            Statuses = { [ReferenceStrategyCatalogue.NativeMultiReference] = ReferenceStrategyResolutionStatus.Possible }
        };

        var seed = await LocationBackdropSlotPrefill.ResolveAsync(strategies, "qwen-image-2.1", blueprint, backdrop);

        Assert.Equal(LocationBackdropSeedOutcome.NoLocationSlot, seed.Outcome);
        Assert.Null(seed.Binding);
        Assert.Contains("Location", seed.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing is proved against a model id that is not chosen yet, so nothing is seeded — and the seed says which
    /// question is unanswered rather than claiming the model cannot carry a reference.
    /// </summary>
    [Fact]
    public async Task Resolve_WithoutAViewModel_SeedsNothing()
    {
        var blueprint = ImageStepBlueprintFactory.ForPackIdentityComposition([]);
        var backdrop = SceneImageLocationBackdrop.Create("loc-a", "img-a", "ABC", 2, "Front");
        var strategies = new IdentityStrategyDecisionTests.MapReferenceStrategies
        {
            Statuses = { [ReferenceStrategyCatalogue.NativeMultiReference] = ReferenceStrategyResolutionStatus.Possible }
        };

        var seed = await LocationBackdropSlotPrefill.ResolveAsync(strategies, null, blueprint, backdrop);

        Assert.Equal(LocationBackdropSeedOutcome.NoModelSelected, seed.Outcome);
        Assert.Null(seed.Binding);
    }
}
