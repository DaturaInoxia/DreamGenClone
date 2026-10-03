using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The model families a LoRA dataset may declare and a LoRA training profile may be qualified for.
///
/// <para>
/// One list, because the family is a MATCH KEY rather than a label: a dataset declares the family its images were
/// rendered for, and training may only pick a profile that was qualified for that same family. Two hand-typed
/// copies of these strings is how a dataset ends up declaring a family no profile can ever match — which is
/// exactly what a free-text field produced (a dataset declaring "IDK").
/// </para>
///
/// <para>
/// The values are the <see cref="SceneImageModelFamily"/> member names, deliberately: the render path already
/// identifies a model by that enum (a cell's model snapshot carries <c>sceneImageModelFamily</c>), so a family
/// spelled any other way names nothing the pipeline can render or train.
/// </para>
/// </summary>
public static class CharacterLoraModelFamilies
{
    /// <summary>
    /// The trainable families, in the order the UI offers them, most-used first.
    ///
    /// <see cref="SceneImageModelFamily.Unknown"/> and <see cref="SceneImageModelFamily.Api"/> are absent on
    /// purpose: one is the ABSENCE of a family and the other is a hosted endpoint. Neither is a LoRA target, and
    /// offering them would let a dataset declare a family nothing can train.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        nameof(SceneImageModelFamily.QwenImage21),
        nameof(SceneImageModelFamily.Sdxl),
        nameof(SceneImageModelFamily.Pony),
        nameof(SceneImageModelFamily.Flux),
        // Krea 2 trains with musubi-tuner, NOT kohya, so a Krea 2 dataset can only be trained by a profile whose
        // trainer id dispatches to the RunPod Serverless worker. Listed last because it is the newest family;
        // the order here is the order the UI offers them.
        nameof(SceneImageModelFamily.Krea2)
    ];

    /// <summary>
    /// Whether <paramref name="family"/> names one of <see cref="All"/>. Ordinal comparison, because the value is
    /// persisted and compared as an exact match key — casings that differ are different families, not close ones.
    /// </summary>
    public static bool IsKnown(string? family) =>
        !string.IsNullOrWhiteSpace(family) && All.Contains(family.Trim(), StringComparer.Ordinal);

    /// <summary>
    /// The refusal every entry point throws for an unusable family. One message, so a dataset, a profile and a
    /// retargeted dataset all say the same thing about what is allowed.
    /// </summary>
    public static string DescribeRefusal(string? family, string subject) =>
        $"{subject} model family '{family}' is not a trainable family. Choose one of: {string.Join(", ", All)}.";
}
