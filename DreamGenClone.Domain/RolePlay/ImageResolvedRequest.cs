using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// One axis as the render ACTUALLY carried it (B-135 B135-013).
///
/// <para>
/// Same vocabulary as <see cref="ImageCellBinding"/> on purpose: the cell declares an axis in this vocabulary and the
/// run records it in this vocabulary, so the comparison is a comparison and not a translation. A separate "resolved"
/// spelling would let the two drift, and a drifted comparison passes for the wrong reason.
/// </para>
///
/// <para>
/// It carries provenance the DECLARATION cannot have — the artifact id, the reference count, the graph actually used —
/// because a resolved binding is a fact about one render, and the facts are what the operator has to read when the run
/// and the cell disagree.
/// </para>
/// </summary>
public sealed record ImageResolvedBinding(
    ImageBindingAxis Axis,
    ImageBindingMode Mode,

    /// <summary>What the axis resolved TO: a LoRA artifact id, a reference asset/pack id, an adapter name.</summary>
    string? Value = null,

    /// <summary>The strength actually applied. Required for <see cref="ImageBindingMode.Lora"/>.</summary>
    double? Strength = null,

    /// <summary>The mechanism actually used (IP-Adapter graph, native multi-reference, OpenPose ControlNet).</summary>
    string? Strategy = null,

    /// <summary>Free provenance for the audit trail — file name, sha256, reference count. Never compared.</summary>
    string? Provenance = null);

/// <summary>
/// What the app ACTUALLY submitted for one render (B-135 B135-013), recorded so the request layer can be checked
/// without a GPU, a network, or the database.
///
/// <para>
/// This is the <i>fact</i> side of the contract: <see cref="ImageSuiteCell.BindingsJson"/> declares what a cell needs,
/// and this records what the render did. Every field here is something the render path already resolves and already
/// audits (<c>SceneImageRequestSubmitted</c>), so recording it is not new work — what is new is that something
/// compares it to the declaration.
/// </para>
///
/// <para>
/// Deliberately NOT part of <see cref="ImageCompilerProfile"/>: a profile describes a checkpoint, and this describes
/// one render against it. Merging them would make a per-checkpoint fact look like a per-run fact, which is the same
/// class of mistake as keying a compiler on a model family.
/// </para>
/// </summary>
public sealed record ImageResolvedRequest
{
    /// <summary>The checkpoint that actually rendered, matching <c>ResolvedImageModel.ModelIdentifier</c>.</summary>
    public string Checkpoint { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public SceneImageModelFamily Family { get; init; } = SceneImageModelFamily.Unknown;

    public SceneImagePromptDialect Dialect { get; init; } = SceneImagePromptDialect.Unknown;

    /// <summary>
    /// The mode the record carried. Not a route enum of its own: a shape-only render and a pose-only ControlNet render
    /// both use <see cref="SceneImageRenderMode.PromptOnly"/>, so the bindings — not this field — say what was applied.
    /// </summary>
    public SceneImageRenderMode RenderMode { get; init; } = SceneImageRenderMode.PromptOnly;

    /// <summary>The negative actually submitted. Empty is the normal case (B-135 D10).</summary>
    public string Negative { get; init; } = string.Empty;

    /// <summary>The seed actually submitted, or null when the render drew a random one.</summary>
    public long? Seed { get; init; }

    /// <summary>"WxH", or null when the render did not state one.</summary>
    public string? Size { get; init; }

    /// <summary>The prompt actually submitted, after placeholder injection and LoRA trigger tokens.</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Every axis the render carried, with the mechanism it used. Empty means a prompt-only render.</summary>
    public IReadOnlyList<ImageResolvedBinding> Bindings { get; init; } = [];
}

/// <summary>
/// What a run asserts about the envelope it did not put in the bindings: the seed and the size the cell declared
/// (B-135 B135-013 D12).
///
/// <para>
/// Both are nullable and BOTH ARE REQUIRED TO BE PASSED, which is the point: passing <c>null</c> is a caller saying "this
/// cell declares no seed" out loud, and the evaluator then reports that as a gap in the cell. A defaulted parameter
/// would mean the same thing silently, and "the cell declares nothing" is exactly the state this whole layer exists to
/// make visible.
/// </para>
/// </summary>
public sealed record ImageRequestExpectation(long? Seed, string? Size);
