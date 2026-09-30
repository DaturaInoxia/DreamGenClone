using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// How much of a body pose may be expressed in prompt TEXT for a given checkpoint (B-135 D18).
///
/// <para>
/// This is a measured property of the checkpoint, not a style preference. The canonical compiler standards
/// record it (§2.6): plain-text prompts get multi-person pose/attribute separation right only some of the
/// time (~75% reported), and the reliable structural fixes are regional prompting and ControlNet
/// OpenPose/Depth — "not a prompt-string tweak". Pony's own instruction file reports the same class of
/// failure ("two female → one figure"). A compiler that keeps trying produces a silently wrong image,
/// which is exactly the failure this capability exists to refuse.
/// </para>
/// </summary>
public enum ImagePoseInText
{
    Unknown = 0,

    /// <summary>Complex and multi-person poses may be described in text.</summary>
    Full = 1,

    /// <summary>Only a simple single-subject pose may be described in text; anything more needs a structural source.</summary>
    SimpleOnly = 2,

    /// <summary>Pose must not be attempted in text at all; a controlled pose needs a structural source.</summary>
    Forbidden = 3
}

/// <summary>
/// One compiler profile per CHECKPOINT (B-135 D13), not per model family.
///
/// <para>
/// Why: <c>ISceneImagePromptCompilerRegistry</c> keys on <c>(SceneImageModelFamily, PromptDialect)</c>, so BigLust
/// and Juggernaut share one compiler, and Qwen-Image-2.1 and FLUX both inject <c>SdxlSceneImagePromptBuilder</c>
/// (the code itself calls this "a documented follow-up"). A prompt budget is not a family property either:
/// BigLust/Juggernaut want small low-detail prompts, Qwen-2.1 wants long detailed ones.
/// </para>
///
/// <para>
/// The profile is CONFIGURATION IN THE DATABASE. The seed rows are migration data, never runtime fallback
/// constants; every field a render needs is read from the row that matched the checkpoint, and a checkpoint with
/// no row fails fast naming the checkpoint (governance rule 4: a model with no researched settings cannot be the
/// target of a compiler).
/// </para>
/// </summary>
public sealed class ImageCompilerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The checkpoint this profile describes, matching <c>RegisteredModel.ModelIdentifier</c>
    /// (e.g. <c>bigLust_v16.safetensors</c>). Matched case-insensitively; two model rows that share a
    /// checkpoint (local + serverless) deliberately share one profile.
    /// </summary>
    public string CheckpointIdentifier { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public SceneImageModelFamily Family { get; set; } = SceneImageModelFamily.Unknown;

    public SceneImagePromptDialect PromptDialect { get; set; } = SceneImagePromptDialect.Unknown;

    /// <summary>Shortest acceptable compiled prompt, in characters.</summary>
    public int MinChars { get; set; }

    /// <summary>Longest acceptable compiled prompt, in characters. The budget is what makes "small prompt" enforceable.</summary>
    public int MaxChars { get; set; }

    /// <summary>
    /// Longest acceptable compiled prompt, in text-encoder tokens. For SDXL-family checkpoints the ceiling is the
    /// CLIP window (77 tokens per encoder, less BOS/EOS), which is a property of the architecture rather than a
    /// preference.
    /// </summary>
    public int MaxTokens { get; set; }

    /// <summary>JSON array of element names that must be present in a compiled prompt for this checkpoint.</summary>
    public string RequiredComponentsJson { get; set; } = "[]";

    /// <summary>
    /// JSON array of tokens that must NOT appear: story names, relationships, ownership, "no X" negations,
    /// and the POV character when the POV is a named observer. A match refuses the prompt.
    /// </summary>
    public string ForbiddenTokensJson { get; set; } = "[]";

    public ImagePoseInText PoseInText { get; set; } = ImagePoseInText.Unknown;

    /// <summary>
    /// The checkpoint's declared negative prompt. EMPTY by default, and empty for every checkpoint except those
    /// with recorded external research — a non-empty value REQUIRES <see cref="NegativeSource"/>.
    ///
    /// <para>
    /// Negatives were purged across this app on 2026-09-08 as VALUES, but the capability survived
    /// (<c>SceneImageStudioSettings.NegativePrompt</c>, a Studio textbox, a compiler-level negative), so they kept
    /// returning. B-135 removes the capability and keeps exactly one cited exception: Pony, whose author guidance
    /// is that Pony IGNORES "no X" in the positive, so negations must live in the negative or they do not happen.
    /// </para>
    /// </summary>
    public string Negative { get; set; } = string.Empty;

    /// <summary>The external research that justifies a non-empty <see cref="Negative"/>. Required when it is not empty.</summary>
    public string? NegativeSource { get; set; }

    /// <summary>JSON object: sampler, scheduler, steps, cfg, resolution and any checkpoint-specific envelope value.</summary>
    public string SettingsEnvelopeJson { get; set; } = "{}";

    /// <summary>The compiler's own system prompt for this checkpoint.</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>JSON array of externally-sourced few-shot examples (governance rule 9).</summary>
    public string ExamplesJson { get; set; } = "[]";

    /// <summary>Where this row's values come from. A value with no source is an unqualified profile and says so.</summary>
    public string ResearchSource { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
