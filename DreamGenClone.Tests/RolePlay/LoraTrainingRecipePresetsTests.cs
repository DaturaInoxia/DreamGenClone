using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The recipes the profile form offers.
///
/// <para>
/// They exist so no operator types a rank, a learning rate or a step count — which only works if each recipe is
/// internally consistent. These tests are what makes it a recipe rather than a bag of numbers: the step cap has to
/// agree with images x repeats x epochs, or the run stops early (or trains epochs it never reaches); the learning
/// rates have to be inside the range both the repository and kohya accept; and the JSON has to carry every field the
/// repository validates, since it is handed straight to it.
/// </para>
/// </summary>
public sealed class LoraTrainingRecipePresetsTests
{
    [Fact]
    public void EveryPresetIsInternallyConsistent()
    {
        Assert.NotEmpty(LoraTrainingRecipePresets.All);
        Assert.Equal(
            LoraTrainingRecipePresets.All.Count,
            LoraTrainingRecipePresets.All.Select(preset => preset.Name).Distinct(StringComparer.Ordinal).Count());

        foreach (var preset in LoraTrainingRecipePresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name), "A preset needs a name to be pickable.");
            Assert.False(string.IsNullOrWhiteSpace(preset.Detail), $"'{preset.Name}' needs a reason to be chosen.");
            Assert.True(preset.ImageCount > 0, $"'{preset.Name}' needs a positive image count.");
            Assert.NotEmpty(preset.ResolutionBuckets);
            Assert.All(preset.ResolutionBuckets, bucket => Assert.True(bucket > 0));

            // The step cap and the epoch count must describe the SAME run.
            Assert.Equal(preset.ImageCount * preset.Repeats * preset.Epochs, preset.Steps);

            Assert.True(preset.Rank > 0);
            Assert.True(preset.Alpha >= 0);
            Assert.InRange(preset.UnetLearningRate, 1e-6, 1e-3);
            Assert.InRange(preset.TextEncoderLearningRate, 1e-6, 1e-3);
            Assert.InRange(preset.CaptionDropout, 0d, 1d);
            Assert.Contains(preset.Precision, new[] { "bf16", "fp16", "no" });

            // Cadence derived from the run, so it can never be set past the end or so often it fills the disk.
            Assert.InRange(preset.CheckpointEverySteps, 1, preset.Steps);
            Assert.InRange(preset.SampleEverySteps, 1, preset.Steps);
        }
    }

    [Fact]
    public void TheDefaultPresetIsTheOneTheFormStartsWith()
    {
        Assert.Same(LoraTrainingRecipePresets.All[0], LoraTrainingRecipePresets.Default);
    }

    [Fact]
    public void TheRecipeJsonCarriesEveryFieldTheRepositoryDemands()
    {
        using var recipe = JsonDocument.Parse(LoraTrainingRecipePresets.Default.ToRecipeJson());
        var root = recipe.RootElement;

        foreach (var field in new[]
        {
            "imageCount", "coverage", "resolutionBuckets", "repeats", "rank", "alpha",
            "unetLearningRate", "textEncoderLearningRate", "steps", "epochs", "captionDropout",
            "priorPreservation", "precision"
        })
        {
            Assert.True(root.TryGetProperty(field, out _), $"The recipe JSON is missing '{field}'.");
        }

        Assert.Equal(JsonValueKind.Object, root.GetProperty("coverage").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("resolutionBuckets").ValueKind);
    }
}
