using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Payload for a text-to-image scene asset generation job.</summary>
public sealed class SceneAssetGenerationJobPayload
{
    public string AssetId { get; set; } = string.Empty;
    public string ImageId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string ImageSize { get; set; } = string.Empty;
    public string? CandidateBatchId { get; set; }
    public string? ReferenceApplicationsJson { get; set; }

    /// <summary>
    /// The sampler seed to render with, or null to draw a fresh one for this image. Null is NOT "no seed" — the
    /// handler draws one and records it on the image — it means the caller asked for a new result rather than a
    /// reproduction. A pinned value makes the render repeatable, given the same prompt, model and sampler recipe.
    /// </summary>
    public long? Seed { get; set; }

    /// <summary>
    /// The character LoRA(s) this render selects, or null for none. Carried on the payload because the LoRA has to
    /// survive the queue: the graph the worker builds reads the resolved model, and a selection that only existed in
    /// the enqueueing request would be lost by the time anything rendered it.
    /// </summary>
    public List<DreamGenClone.Web.Application.RolePlay.Models.SceneImageCharacterLoraSelection>? CharacterLoras { get; set; }

    /// <summary>
    /// The NON-IDENTITY scene LoRA(s) this render selects (unlock / act / anatomy / style), or null for none.
    /// Carried on the payload for the same reason <see cref="CharacterLoras"/> is: the selection has to survive the
    /// queue, or it is lost before anything renders. Resolved and family-checked by <c>SceneLoraResolver</c> in the
    /// handler, so an unknown file or a LoRA from another family fails by name instead of being dropped.
    /// </summary>
    public List<DreamGenClone.Web.Application.RolePlay.Models.SceneImageLoraSelection>? SceneLoras { get; set; }

    /// <summary>
    /// The lighting / expression presets this render applied, with the clause each one contributed. Carried on the
    /// payload so the completion step records the operator's actual choice rather than re-deriving it from the prompt
    /// (B-140 D4).
    /// </summary>
    public List<DreamGenClone.Web.Application.RolePlay.Models.AppliedImagePreset>? AppliedPresets { get; set; }

    /// <summary>
    /// The description the OPERATOR typed for this render, or null when they wrote the prompt itself. Carried on the
    /// payload so the completion step records it on the image: the row's <c>Prompt</c> is what RENDERS (the compiled
    /// text once a compiler has authored it), so without this the operator's own words are lost the moment "Generate
    /// Prompt" runs, and "Your input" would come back from a round-trip holding the generated prompt instead.
    /// </summary>
    public string? UserInput { get; set; }

    /// <summary>The caller-declared tags (character / position / wardrobe / location / sex position) for this render.</summary>
    public List<string>? DeclaredTags { get; set; }

    /// <summary>
    /// The verified stance whose OpenPose skeleton conditions this render, or null for a plain text-to-image call.
    /// Null means "no pose was asked for" — never "use a default pose".
    /// </summary>
    public string? PoseStance { get; set; }

    /// <summary>ControlNet conditioning strength for <see cref="PoseStance"/>, required exactly when it is set.</summary>
    public double? PoseStrength { get; set; }

    /// <summary>
    /// A POSE LIBRARY preset whose own skeleton conditions this render, or null when the pose (if any) comes from a
    /// stance. This is the pose-library route (B-130 §020), and it is deliberately separate from
    /// <see cref="PoseStance"/> rather than another stance value:
    ///
    /// <para>
    /// A STANCE is a canonical body pose the body card owns and the provider has verified, addressed by enum and
    /// resolved through <c>BodyStanceSkeletons</c>. A LIBRARY PRESET is a named skeleton an operator picked, whose
    /// artifact is the same file the pose proofs used — which is what makes an image built from it reproducible outside
    /// the app. No enum can name that file, and fusing the two vocabularies would mean either every library pose needs
    /// a stance added, or a stance could silently resolve to a preset nobody selected.
    /// </para>
    ///
    /// <para>Exactly one of this and <see cref="PoseStance"/> may be set; both is a duplicate pose channel and fails.</para>
    /// </summary>
    public string? PosePresetId { get; set; }

    /// <summary>
    /// The preset's skeleton path, recorded for provenance only. The handler resolves the BYTES from
    /// <see cref="PosePresetId"/> through the pose library, so a stale path in a payload cannot make the render read a
    /// different file than the preset it names.
    /// </summary>
    public string? PoseSkeletonRelativePath { get; set; }

    /// <summary>
    /// The approved identity pack this render is conditioned on, or null for an unconditioned render. The HANDLER
    /// re-reads the pack and its face asset, so an unconditional fallback is impossible: a pack that is no longer
    /// approved fails the render rather than quietly producing a different person.
    /// </summary>
    public string? IdentityPackId { get; set; }

    /// <summary>The approved face asset within <see cref="IdentityPackId"/> to condition on.</summary>
    public string? IdentityFaceAssetId { get; set; }

    /// <summary>
    /// The approved identity pack the BODY reference comes from, or null when the render carries no body
    /// reference. Stated separately from <see cref="IdentityPackId"/> because a view from directly behind has no
    /// face in frame and therefore no face reference, yet still conditions on the character's build.
    /// </summary>
    public string? BodyReferencePackId { get; set; }

    /// <summary>
    /// The approved full-body asset within <see cref="BodyReferencePackId"/> whose view and state match this
    /// cell. The handler re-reads it and its bytes, so a reference that was unapproved or replaced between the
    /// queue and the render fails the render rather than quietly rendering a different build.
    /// </summary>
    public string? BodyReferenceAssetId { get; set; }

    /// <summary>
    /// The canonical angle this render is asked for, or null when the render is not an angle render. The handler
    /// resolves the skeleton from the committed angle library by this value, so an angle whose skeleton does not
    /// exist fails the render rather than turning the body by inference.
    /// </summary>
    public string? BodyAngleView { get; set; }

    /// <summary>
    /// The ACCEPTED body image this angle render is based on, required exactly when <see cref="BodyAngleView"/> is
    /// set. The handler re-reads the image row and its bytes, so a source that was deleted or replaced between the
    /// queue and the render fails the render instead of producing a different body.
    /// </summary>
    public string? BodyAngleSourceImageId { get; set; }
}

/// <summary>Payload for a typed-vision reference candidate generation job.</summary>
public sealed record ProducedImageGenerationJobPayload
{
    public string BatchId { get; init; } = string.Empty;
    public string TargetRef { get; init; } = string.Empty;
    public ProducedImageReferenceKind ReferenceKind { get; init; }
    public string VisionText { get; init; } = string.Empty;
    public string? ModelId { get; init; }
    public string ImageSize { get; init; } = string.Empty;
}

/// <summary>Payload for a Qwen source-image edit that produces a new scene asset revision.</summary>
public sealed class SceneAssetEditingJobPayload
{
    public string AssetId { get; set; } = string.Empty;
    public string ImageId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? CandidateBatchId { get; set; }
    public string? ReferenceApplicationsJson { get; set; }
}

/// <summary>
/// Payload for the special "Generate Profile Pack" function: generate the 5 face views (front +
/// 3/4L, 3/4R, profL, profR) for one scenario character and save them into a draft identity pack
/// plus the asset library.
/// </summary>
public sealed class SceneAssetProfilePackJobPayload
{
    /// <summary>The scenario character this pack belongs to (characterProfileId).</summary>
    public string CharacterProfileId { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    /// <summary>Visual description used when no <see cref="FrontAssetId"/> is supplied.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>An existing complete asset to use as the front (identity anchor); skips front generation.</summary>
    public string? FrontAssetId { get; set; }

    /// <summary>Exact generation model used when the front image is generated.</summary>
    public string? FrontModelId { get; set; }

    /// <summary>Exact source-image editor model used for the four angle views.</summary>
    public string EditorModelId { get; set; } = string.Empty;

    /// <summary>Optional target draft pack; when null the job ensures a draft for the character.</summary>
    public string? IdentityPackId { get; set; }
}
