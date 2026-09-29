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
            Precision: "bf16")
    ];

    /// <summary>The recipe offered first, and the only one a caller may rely on existing.</summary>
    public static LoraTrainingRecipePreset Default => All[0];
}
