namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// What <c>PrepareContinuationAsync</c> resolves for a "continue this clip" action (B-156): the source clip, the
/// extracted anchor frame that is hashed and stored, where the new composition sits in its chain, and the settings
/// to clone. It is a SEED - it never creates a record; the composer creates one when the operator queues.
/// </summary>
/// <param name="Source">The finished clip being continued.</param>
/// <param name="Kind">Whether the source's audio is carried forward as well as its final frame.</param>
/// <param name="FrameRelativePath">The extracted frame's path, relative to the SCENE-VIDEO root.</param>
/// <param name="FrameSha256">The extracted frame's SHA-256, so the anchor is provably the frame it claims to be.</param>
/// <param name="FrameFileName">The name the frame is uploaded to ComfyUI under (derived from the sha).</param>
/// <param name="GuideFrameIndex">The frame index the guide pins in the NEW clip. Configured, never inferred.</param>
/// <param name="ChainDepth">
/// How many continuations this new clip would be (1 = the first continuation of an original render). Compared
/// against the configured budget before anything is queued.
/// </param>
/// <param name="ChainRootVideoId">The first clip of this chain, used as the tone reference for drift reporting.</param>
/// <param name="SourceDurationSeconds">The source's measured duration, for the banner and the total-length statement.</param>
/// <param name="AudioRelativePath">
/// When the kind is <see cref="SceneVideoContinuationKind.FrameAndAudio"/>, the extracted source audio's path
/// relative to the scene-video root; null for a frame-only continuation.
/// </param>
public sealed record SceneVideoContinuationSeed(
    SceneVideoRecord Source,
    SceneVideoContinuationKind Kind,
    string FrameRelativePath,
    string FrameSha256,
    string FrameFileName,
    int GuideFrameIndex,
    int ChainDepth,
    string ChainRootVideoId,
    double? SourceDurationSeconds,
    string? AudioRelativePath = null);

/// <summary>
/// Per-block drift evidence for a continuation (B-156 C-14). Chain drift is documented as roughly +4 % contrast
/// bloom and ~1/3 treble loss per join, but the published figures come from small, partly contradictory
/// measurements - so these are recorded as EVIDENCE, on the operator's own content, rather than used as a gate.
/// Every member is nullable: a metric ffmpeg could not produce is absent, never guessed.
/// </summary>
/// <param name="LuminanceMean">Mean luma of the delivered clip (0-255).</param>
/// <param name="LuminanceLowPercentile">10th-percentile luma, the shadow end where the documented crush happens.</param>
/// <param name="LuminanceHighPercentile">90th-percentile luma, the highlight end where blow-out happens.</param>
/// <param name="SaturationMean">Mean chroma, the "more contrasty / more saturated" axis of the reported drift.</param>
/// <param name="NearBlackPercent">Percentage of near-black pixels; the documented failure reaches ~2 % by clip 8.</param>
/// <param name="EdgeEnergyMean">Mean absolute edge energy - a sharpness proxy, comparable only as a RATIO to the
/// source frame's value (which is why <paramref name="EdgeEnergyRatioToSource"/> is the number that matters).</param>
/// <param name="EdgeEnergyRatioToSource">This block's edge energy over the source frame's; 1.0 means unchanged.</param>
/// <param name="AudioTrebleToBassRatio">High-frequency RMS over low-frequency RMS. The documented failure loses
/// about a third of this per join, and it is inaudible in a waveform.</param>
/// <param name="Notes">Anything the measurement could not produce, stated rather than silently omitted.</param>
public sealed record SceneVideoDriftMetrics(
    double? LuminanceMean,
    double? LuminanceLowPercentile,
    double? LuminanceHighPercentile,
    double? SaturationMean,
    double? NearBlackPercent,
    double? EdgeEnergyMean,
    double? EdgeEnergyRatioToSource,
    double? AudioTrebleToBassRatio,
    string? Notes);
