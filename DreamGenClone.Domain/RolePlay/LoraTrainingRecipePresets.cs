using System.Text.Json;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// One complete, named LoRA training recipe.
///
/// <para>
/// It exists so no operator ever types rank, alpha, learning rates, steps, epochs or dropout. Those numbers are a
/// recipe, not a preference: they only mean anything together, and a form that asks for them one box at a time lets
/// someone pair a rank of 128 with a text-encoder rate of 1e-3 and call it a run. A profile is created by naming a
/// checkpoint and picking a recipe; the numbers come from here.
/// </para>
/// </summary>
public sealed record LoraTrainingRecipePreset(
    string Name,
    string Detail,
    int ImageCount,
    IReadOnlyList<int> ResolutionBuckets,
    int Repeats,
    int Rank,
    int Alpha,
    double UnetLearningRate,
    double TextEncoderLearningRate,
    int Steps,
    int Epochs,
    double CaptionDropout,
    bool PriorPreservation,
    string Precision)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// How often to write an intermediate checkpoint: a quarter of the run. Derived rather than asked for, so it
    /// cannot be set beyond the end of the run (which would save nothing) or so often that the disk fills.
    /// </summary>
    public int CheckpointEverySteps => Math.Max(1, Steps / 4);

    /// <summary>How often to render a sample image: an eighth of the run, so four checkpoints get two samples each.</summary>
    public int SampleEverySteps => Math.Max(1, Steps / 8);

    /// <summary>
    /// The recipe JSON a training profile stores.
    ///
    /// <c>steps</c> is derived as images x repeats x epochs rather than chosen separately, so the step cap and the
    /// epoch count cannot disagree: a fixed step count with a larger epoch count is a run that silently stops early,
    /// and a smaller one trains for epochs it never reaches.
    /// </summary>
    public string ToRecipeJson() => JsonSerializer.Serialize(new
    {
        imageCount = ImageCount,
        coverage = new { cells = ImageCount },
        resolutionBuckets = ResolutionBuckets,
        repeats = Repeats,
        rank = Rank,
        alpha = Alpha,
        unetLearningRate = UnetLearningRate,
        textEncoderLearningRate = TextEncoderLearningRate,
        steps = Steps,
        epochs = Epochs,
        captionDropout = CaptionDropout,
        priorPreservation = PriorPreservation,
        precision = Precision
    }, JsonOptions);
}

/// <summary>
/// The recipes the app offers. Every one is a complete, self-consistent set; editing them is how a recipe changes,
/// not typing into the form.
/// </summary>
public static class LoraTrainingRecipePresets
{
    /// <summary>
    /// Ordered most-appropriate-first for the usual case (a character set of ~36 images on a 16 GB SDXL host).
    /// </summary>
    public static readonly IReadOnlyList<LoraTrainingRecipePreset> All =
    [
        new LoraTrainingRecipePreset(
            Name: "SDXL character — standard",
            Detail: "36 images, rank 32. The default for a full coverage set.",
            ImageCount: 36,
            ResolutionBuckets: [1024],
            Repeats: 10,
            Rank: 32,
            Alpha: 16,
            UnetLearningRate: 1e-4,
            TextEncoderLearningRate: 1e-5,
            Steps: 36 * 10 * 8,
            Epochs: 8,
            CaptionDropout: 0.05,
            PriorPreservation: false,
            Precision: "bf16"),

        new LoraTrainingRecipePreset(
            Name: "SDXL character — gentle (rank 16)",
            Detail: "For a set that yields a face the standard recipe over-bakes; less capacity, less style bleed.",
            ImageCount: 36,
            ResolutionBuckets: [1024],
            Repeats: 8,
            Rank: 16,
            Alpha: 16,
            UnetLearningRate: 1e-4,
            TextEncoderLearningRate: 1e-5,
            Steps: 36 * 8 * 6,
            Epochs: 6,
            CaptionDropout: 0.05,
            PriorPreservation: false,
            Precision: "bf16"),

        new LoraTrainingRecipePreset(
            Name: "SDXL character — stronger (rank 64, longer)",
            Detail: "For a set whose identity does not take at rank 32. More capacity and more steps, more risk.",
            ImageCount: 36,
            ResolutionBuckets: [1024],
            Repeats: 12,
            Rank: 64,
            Alpha: 32,
            UnetLearningRate: 1e-4,
            TextEncoderLearningRate: 1e-5,
            Steps: 36 * 12 * 10,
            Epochs: 10,
            CaptionDropout: 0.10,
            PriorPreservation: false,
            Precision: "bf16"),

        new LoraTrainingRecipePreset(
            Name: "SDXL character — small set (24 images)",
            Detail: "For a partial set; more repeats so the step count still reaches the same order of magnitude.",
            ImageCount: 24,
            ResolutionBuckets: [1024],
            Repeats: 15,
            Rank: 32,
            Alpha: 16,
            UnetLearningRate: 1e-4,
            TextEncoderLearningRate: 1e-5,
            Steps: 24 * 15 * 8,
            Epochs: 8,
            CaptionDropout: 0.05,
            PriorPreservation: false,
            Precision: "bf16"),

        // Krea 2 does NOT train with kohya. These numbers are the musubi-tuner recipe proven on the local 16 GB
        // RTX 5080 (2026-09-27/28), dispatched to RunPod Serverless by lora_train_service.py.
        //
        // Two fields cannot express "not applied" and are therefore carried as INERT values, called out in the
        // Detail string so an operator sees it exactly where the recipe is chosen:
        //   TextEncoderLearningRate - the recipe type REQUIRES a rate in [1e-6, 1e-3] (asserted by
        //     LoraTrainingRecipePresetsTests, because kohya and the repository both demand one), yet the Krea 2
        //     path trains the LoRA network only. The value is never sent to the worker.
        //   CaptionDropout 0 - musubi's krea2 trainer is given no caption-dropout argument.
        //
        // Flow shift (2.5), fp8_base/fp8_scaled and blocks_to_swap are deliberately NOT fields here: they are
        // properties of how the worker invokes musubi, not of the app's training profile, and recording them at
        // this level would claim the app controls something it does not.
        //
        // Steps are deliberately conservative (576). The endpoint enforces a 4h execution timeout and the real
        // serverless s/step is not yet measured - raise this once a run has been timed.
        new LoraTrainingRecipePreset(
            Name: "Krea 2 character — serverless (musubi-tuner)",
            Detail: "36 images, rank 32, alpha 32, 576 steps. Trains on RunPod Serverless, not on this host. "
                + "The text-encoder rate and caption dropout are carried for the profile contract but are NOT "
                + "applied on this family.",
            ImageCount: 36,
            ResolutionBuckets: [1024],
            Repeats: 2,
            Rank: 32,
            Alpha: 32,
            UnetLearningRate: 1e-4,
            TextEncoderLearningRate: 1e-5,
            Steps: 36 * 2 * 8,
            Epochs: 8,
            CaptionDropout: 0,
            PriorPreservation: false,
            Precision: "bf16")
    ];

    /// <summary>The recipe offered first, and the only one a caller may rely on existing.</summary>
    public static LoraTrainingRecipePreset Default => All[0];
}
