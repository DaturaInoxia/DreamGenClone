using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The step-kind feature table (B-130). What is pinned here is that the features a step offers are a property of the
/// STEP KIND, in one place - because the defect this table replaced was that they were a property of whichever host
/// happened to pass the matching parameter, so the same step looked different on every surface and a host that forgot
/// one shipped a poorer UI without anything failing.
/// </summary>
public sealed class ImageStepProfileTests
{
    [Fact]
    public void ForKind_GivesTheAuthoringStepsACompilePathAndPresets()
    {
        var asset = ImageStepProfile.ForKind(ImageStepKind.AssetCreate);

        Assert.True(asset.Allows(ImageStepFeature.PromptPanel));
        Assert.True(asset.Allows(ImageStepFeature.CompilePrompt));
        Assert.True(asset.Allows(ImageStepFeature.Presets));
        Assert.True(asset.Allows(ImageStepFeature.CharacterLoras));

        Assert.True(ImageStepProfile.ForKind(ImageStepKind.Edit).Allows(ImageStepFeature.CompilePrompt));
    }

    /// <summary>
    /// A training cell must NOT offer the character LoRA picker: rendering the dataset WITH the LoRA being trained is
    /// circular. That was a comment inside one workspace; it is a property of the step kind now, so no other surface
    /// can reintroduce it by passing the parameter.
    /// </summary>
    [Fact]
    public void ForKind_WithholdsCharacterLorasFromATrainingCell()
    {
        var cell = ImageStepProfile.ForKind(ImageStepKind.LoraCell);

        Assert.False(cell.Allows(ImageStepFeature.CharacterLoras));
        Assert.True(cell.Allows(ImageStepFeature.CompilePrompt));
        Assert.True(cell.Allows(ImageStepFeature.Presets));
    }

    /// <summary>
    /// The EDIT step is the only kind that declares scene LoRAs. Its workspace renders no picker of its own, while the
    /// compose, asset-create, playground and body-view hosts already render one beside the composer — declaring the
    /// feature there as well would put two pickers over one value.
    /// </summary>
    [Fact]
    public void ForKind_GivesOnlyTheEditStepTheSceneLoraPicker()
    {
        Assert.True(ImageStepProfile.ForKind(ImageStepKind.Edit).Allows(ImageStepFeature.SceneLoras));

        foreach (var kind in Enum.GetValues<ImageStepKind>())
        {
            if (kind == ImageStepKind.Edit)
            {
                continue;
            }

            Assert.False(
                ImageStepProfile.ForKind(kind).Allows(ImageStepFeature.SceneLoras),
                $"{kind} already renders its own scene-LoRA picker beside the composer.");
        }
    }

    /// <summary>
    /// A composition's prompt arrives from the moment it was opened and its host presents it, so the step does not
    /// present the composer's prompt panel - and therefore cannot offer a compile path either.
    /// </summary>
    [Fact]
    public void ForKind_WithholdsThePromptPanelAndCompilePathFromAComposition()
    {
        var compose = ImageStepProfile.ForKind(ImageStepKind.Compose);

        Assert.False(compose.Allows(ImageStepFeature.PromptPanel));
        Assert.False(compose.Allows(ImageStepFeature.CompilePrompt));
        Assert.True(compose.Allows(ImageStepFeature.CharacterLoras));
    }

    /// <summary>
    /// Every body view offers the character LoRA picker (operator request, 2026-10-07): identity can travel as a LoRA
    /// on the body render just as it does on every other authoring step, so the BodyView kind declares the same
    /// capability instead of being the one surface without it.
    /// </summary>
    /// <summary>
    /// Every body view offers the character LoRA picker (operator request, 2026-10-07): identity can travel as a LoRA
    /// on the body render just as it does on every other authoring step, so the BodyView kind declares the same
    /// capability instead of being the one surface without it. Like every other step, the picker still gates on the
    /// model declaring and qualifying the Lora reference strategy — the scene LoRAs are the separate scene-LoRA picker.
    /// </summary>
    [Fact]
    public void ForKind_GivesTheBodyViewTheCharacterLoraPicker()
    {
        var body = ImageStepProfile.ForKind(ImageStepKind.BodyView);

        Assert.True(body.Allows(ImageStepFeature.PromptPanel));
        Assert.True(body.Allows(ImageStepFeature.CompilePrompt));
        Assert.True(body.Allows(ImageStepFeature.CharacterLoras));
    }

    /// <summary>
    /// A step kind nobody has decided about gets the always-on features and NOTHING optional, so a new kind cannot
    /// silently acquire a control by being absent from the table.
    /// </summary>
    [Fact]
    public void ForKind_GivesAnUndecidedStepKindNoOptionalFeatures()
    {
        var undecided = ImageStepProfile.ForKind((ImageStepKind)999);

        Assert.True(undecided.Allows(ImageStepFeature.PromptPanel));
        Assert.False(undecided.Allows(ImageStepFeature.CompilePrompt));
        Assert.False(undecided.Allows(ImageStepFeature.Presets));
        Assert.False(undecided.Allows(ImageStepFeature.CharacterLoras));
    }

    /// <summary>Every feature the table can grant is reachable by some step kind, or it is dead config.</summary>
    [Fact]
    public void ForKind_GrantsEveryDeclaredFeatureToAtLeastOneStepKind()
    {
        var granted = Enum.GetValues<ImageStepKind>()
            .Select(ImageStepProfile.ForKind)
            .Aggregate(ImageStepFeature.None, (all, profile) => all | profile.Features);

        foreach (var feature in Enum.GetValues<ImageStepFeature>())
        {
            if (feature == ImageStepFeature.None)
            {
                continue;
            }

            Assert.True(granted.HasFlag(feature), $"No step kind offers {feature}, so the flag is dead config.");
        }
    }
}
