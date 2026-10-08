using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// One reference image as the graph consumes it: bytes plus the filename ComfyUI stores it under.
/// <para>
/// Order is SEMANTIC. The <c>MiniMaxH3ReferenceToVideo</c> node requires the compiled prompt's
/// <c>&lt;Picture i&gt;</c> tag to match the reference slot order, so the caller must pass the operator's
/// chosen order and the client must upload and bind it in exactly that order.
/// </para>
/// </summary>
public sealed record SceneVideoReferenceImage(byte[] Content, string FileName);

/// <summary>Frame index the guide pins by default: the first frame of the new clip.</summary>
public sealed record SceneVideoGuideImage(byte[] Content, string FileName);

/// <summary>
/// Everything one H3 render needs beyond the resolved model: the compiled prompt, the ordered references, the
/// canvas, the sampling values the operator chose, and the ordered LoRA stack.
/// </summary>
/// <param name="Prompt">The compiled six-section H3 prompt document.</param>
/// <param name="References">Ordered reference images (slot 0 first). Never reordered by the client.</param>
/// <param name="Width">Canvas width; must be a multiple of 32.</param>
/// <param name="Height">Canvas height; must be a multiple of 32.</param>
/// <param name="Length">Frame count; validated against the model's configured frame policy.</param>
/// <param name="Steps">Sampler steps.</param>
/// <param name="Seed">Fixed seed, persisted with the attempt so a render is reproducible.</param>
/// <param name="RefImageSize"><c>match</c> or <c>max</c>; <c>max</c> is several times slower.</param>
/// <param name="OutputPrefix">ComfyUI output prefix for the produced file (the record's own folder).</param>
/// <param name="Loras">The ordered LoRA stack. Empty means NO LoRA: no loader node is emitted.</param>
/// <param name="GuideImage">
/// Continuation only (B-156): the source clip's final frame, pinned by <c>MiniMaxH3AddGuide</c> at
/// <paramref name="GuideFrameIndex"/>. Null means a plain render and the guide node is not emitted at all.
/// </param>
/// <param name="GuideAudio">
/// Continuation only: the source clip's audio, so the model CONTINUES the previous track rather than starting a new
/// one. Null with a non-null <paramref name="GuideImage"/> means frame-only continuation.
/// </param>
/// <param name="GuideFrameIndex">Which frame of the NEW clip the guide pins. Configured, never inferred.</param>
public sealed record SceneVideoGenerationRequest(
    string Prompt,
    IReadOnlyList<SceneVideoReferenceImage> References,
    int Width,
    int Height,
    int Length,
    int Steps,
    long Seed,
    string RefImageSize,
    string OutputPrefix,
    IReadOnlyList<ResolvedSceneLora>? Loras = null,
    SceneVideoGuideImage? GuideImage = null,
    SceneVideoGuideImage? GuideAudio = null,
    int? GuideFrameIndex = null);

/// <summary>The produced clip plus the ComfyUI output filename it was fetched from.</summary>
public sealed record SceneVideoRenderResult(byte[] VideoBytes, string OutputFileName);

/// <summary>
/// The video-generation boundary. Implemented by the ComfyUI adapter in <c>Infrastructure</c>; the composer and
/// the durable handler depend on this interface only (constitution: adapters live behind an Application
/// interface).
/// </summary>
public interface IVideoGenerationClient
{
    /// <summary>
    /// Render one clip: upload the references, submit the H3 graph, wait for its history entry and return the
    /// produced video bytes. Throws <see cref="ImageGenerationException"/> with a video reason code on failure.
    /// </summary>
    Task<SceneVideoRenderResult> GenerateAsync(
        ResolvedVideoModel model,
        SceneVideoGenerationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight reachability probe. A render is ~25 minutes, so this checks <c>/system_stats</c> plus the
    /// presence of the H3 node (and the configured artifacts) instead of generating anything.
    /// </summary>
    Task<(bool Success, string Message)> CheckVideoModelHealthAsync(
        ResolvedVideoModel model,
        CancellationToken cancellationToken = default);
}
