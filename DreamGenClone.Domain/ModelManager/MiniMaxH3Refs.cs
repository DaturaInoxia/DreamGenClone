namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// The frame-length envelope MiniMax H3 accepts, read from the model's configuration.
/// </summary>
/// <remarks>
/// The node documents <c>min 5 / max 3600 / step 17</c> while its TRAINED range is narrower. Both are carried
/// explicitly so the UI can offer the node's legal values, mark the trained band, and warn above the proven
/// length - and so nothing in code has to know a magic number. 56 frames is accepted by the node yet untrained,
/// which is exactly the kind of value a hardcoded range would silently allow.
/// </remarks>
/// <param name="MinFrames">Node minimum (<c>length</c> lower bound).</param>
/// <param name="MaxFrames">Node maximum (<c>length</c> upper bound).</param>
/// <param name="FrameStep">Node step; a legal length satisfies <c>(length - MinFrames) % FrameStep == 0</c>.</param>
/// <param name="TrainedMinFrames">Lowest trained length (the shortest proven preset).</param>
/// <param name="TrainedMaxFrames">Highest trained length. Anything above it is unproven, not invalid.</param>
/// <param name="WarnAboveFrames">Length above which the composer warns (the cost grows ~linearly).</param>
/// <param name="PresetShortFrames">The short proven preset.</param>
/// <param name="PresetLongFrames">The long proven preset.</param>
public sealed record MiniMaxH3FramePolicy(
    int MinFrames,
    int MaxFrames,
    int FrameStep,
    int TrainedMinFrames,
    int TrainedMaxFrames,
    int WarnAboveFrames,
    int PresetShortFrames,
    int PresetLongFrames)
{
    /// <summary>
    /// Whether <paramref name="frames"/> is a value the node accepts. Congruence is checked against the node's
    /// own bounds rather than assumed, so an illegal length is refused before a ~25 minute submission.
    /// </summary>
    public bool IsAccepted(int frames) =>
        frames >= MinFrames
        && frames <= MaxFrames
        && FrameStep > 0
        && (frames - MinFrames) % FrameStep == 0;

    /// <summary>Whether <paramref name="frames"/> is inside the trained band.</summary>
    public bool IsTrained(int frames) =>
        frames >= TrainedMinFrames && frames <= TrainedMaxFrames;
}

/// <summary>
/// The MiniMax H3 (Ref2VA) artifacts and fixed sampling envelope a video render needs, read from the model's
/// <c>CapabilityQualificationsJson</c> at resolution time.
/// </summary>
/// <remarks>
/// H3 is a SPLIT model - a pruned diffusion transformer, a 32B quantized text encoder (which has to run on the
/// CPU to fit 16 GB), an int8 video VAE, an fp32 audio VAE - behind separate loader nodes, and every artifact
/// name is required: none of them is derivable from the model identifier, and a guessed name is a 400 from
/// ComfyUI.
///
/// <para>
/// Both VAEs are REQUIRED even though <c>MiniMaxH3ReferenceToVideo</c> marks <c>vae</c> and <c>audio_vae</c>
/// optional. Omitting them does not fail loudly - the node silently degrades reference conditioning to
/// text-encoder-only - so the graph builder refuses a graph that binds references without the matching VAE
/// instead of trusting the omission to be harmless.
/// </para>
///
/// <para>
/// The sampler envelope is a MODEL property, not a studio preference: <c>euler</c> + <c>beta</c> is required by
/// the AfterMidnight Ref2VA LoRAs (other schedulers break the generated audio). The graph carries no CFG at all
/// (<c>BasicGuider</c> has no such input) because the distilled model has no negative branch.
/// </para>
/// </remarks>
/// <param name="DitName">Ref2VA diffusion transformer filename (<c>UNETLoader.unet_name</c>).</param>
/// <param name="TextEncoderName">Text encoder filename (<c>CLIPLoader.clip_name</c>, type <c>minimax</c>).</param>
/// <param name="TextEncoderDevice">Where the text encoder runs; the 16 GB recipe pins <c>cpu</c>.</param>
/// <param name="VideoVaeName">Video VAE filename (<c>VAELoader</c>, bound to the node's <c>vae</c>).</param>
/// <param name="AudioVaeName">Audio VAE filename (<c>VAELoader</c>, bound to the node's <c>audio_vae</c>).</param>
/// <param name="SamplerName">Sampler (<c>KSamplerSelect.sampler_name</c>); the qualified value is <c>euler</c>.</param>
/// <param name="Scheduler">Scheduler (<c>BasicScheduler.scheduler</c>); the qualified value is <c>beta</c>.</param>
/// <param name="Steps">Sampler steps. Audio converges later than the picture, so low-step builds are not equivalent.</param>
/// <param name="Denoise">Denoise strength; a full render is 1.</param>
/// <param name="Fps">Frames per second baked into <c>CreateVideo</c> (node-fixed at 24).</param>
/// <param name="BitDepth">Pixel bit depth for <c>CreateVideo</c>.</param>
/// <param name="DefaultWidth">Proven canvas width (the model's own node default is 1344).</param>
/// <param name="DefaultHeight">Proven canvas height (the model's own node default is 768).</param>
/// <param name="DefaultRefImageSize"><c>match</c> or <c>max</c>; the proven default is <c>match</c>.</param>
/// <param name="FramePolicy">The accepted and trained frame-length envelope.</param>
/// <param name="MaxReferenceImages">Node cap on <c>ref_images</c> (9).</param>
/// <param name="MaxReferenceVideos">Node cap on <c>ref_videos</c> (3).</param>
/// <param name="MaxReferenceAudios">Node cap on <c>ref_audios</c> (3).</param>
/// <param name="LoudnessTargetLufs">
/// The integrated loudness the delivered clip is normalized to. Required: H3's native audio measures roughly
/// -24 to -30 LUFS, which is ~12 dB below delivery level, so a clip that is not normalized is easy to mistake
/// for silent. Normalization is mandatory, so there is no "skip" value.
/// </param>
/// <param name="RenderTimeoutSeconds">
/// The polling budget for ONE render. Required and configured per model because a trained-range clip takes ~25 to
/// ~100 minutes while a chat-oriented provider timeout is measured in seconds: borrowing that value would abort
/// every render. Not a socket timeout - it bounds how long the client may wait for the job's history entry.
/// </param>
/// <param name="FfmpegPath">
/// The ffmpeg executable that normalizes the delivered audio and measures stream/loudness evidence. Required: H3's
/// native audio is ~12 dB below delivery level, so normalization is mandatory and a silent skip is not an option -
/// when the configured binary is missing the render FAILS naming the path. This machine has no system ffmpeg, so the
/// configured value is usually the binary bundled with the repo venv's imageio-ffmpeg.
/// </param>
public sealed record MiniMaxH3Refs(
    string DitName,
    string TextEncoderName,
    string TextEncoderDevice,
    string VideoVaeName,
    string AudioVaeName,
    string SamplerName,
    string Scheduler,
    int Steps,
    double Denoise,
    int Fps,
    int BitDepth,
    int DefaultWidth,
    int DefaultHeight,
    string DefaultRefImageSize,
    MiniMaxH3FramePolicy FramePolicy,
    int MaxReferenceImages,
    int MaxReferenceVideos,
    int MaxReferenceAudios,
    double LoudnessTargetLufs,
    int RenderTimeoutSeconds,
    string FfmpegPath,
    /// <summary>
    /// How many continuations a chain may add before the operator is told to start a fresh chain (B-156 C-13).
    /// Configured, never defaulted: the measured drift is ~+4 % contrast bloom and ~1/3 treble loss per join,
    /// and independent projects put the comfortable zone at 3-4 joins.
    /// </summary>
    int MaxContinuationChainLength);
