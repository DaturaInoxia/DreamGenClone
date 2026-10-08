using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// Finishing and verification for a produced clip (B-152 S6). H3 emits picture and audio in one pass, and its
/// native audio measures ~12 dB below delivery level, so the raw render is not the deliverable: it is normalized to
/// the model's configured loudness target and then MEASURED, so the queue can show what was actually produced.
///
/// <para>
/// Implemented by shelling out to a configured ffmpeg binary. When the configured binary is absent the call fails
/// with the path named - normalization is mandatory, and a silent skip would ship an inaudible clip that looks
/// exactly like a successful render.
/// </para>
/// </summary>
public interface IVideoAudioProcessor
{
    /// <summary>Reads the streams, duration and integrated loudness of an existing file.</summary>
    Task<SceneVideoAudioEvidence> ProbeAsync(
        string absolutePath, string ffmpegPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a loudness-normalized copy beside the raw render and returns its path plus measured evidence for the
    /// NORMALIZED file.
    /// </summary>
    Task<(string NormalizedAbsolutePath, SceneVideoAudioEvidence Evidence)> NormalizeAndVerifyAsync(
        string absolutePath,
        string ffmpegPath,
        double targetLufs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts the LAST frame of a finished clip to <paramref name="destinationAbsolutePath"/> (B-156 C-4). That
    /// frame is the anchor a continuation starts from, so extraction is deterministic and idempotent for a given
    /// clip - the same input always yields the same file.
    /// </summary>
    Task ExtractFinalFrameAsync(
        string absolutePath,
        string destinationAbsolutePath,
        string ffmpegPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a clip's audio to a WAV (B-156 C-15), so a continuation can carry the previous track forward
    /// instead of restarting it. Written at a fixed 32 kHz stereo PCM, which is what the H3 node's audio path takes.
    /// </summary>
    Task ExtractAudioAsync(
        string absolutePath,
        string destinationAbsolutePath,
        string ffmpegPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Measures per-block drift evidence for a delivered clip (B-156 C-14). The reported degradation when clips are
    /// chained is roughly +4 % contrast bloom and ~1/3 treble loss per join, but those figures come from small,
    /// partly contradictory measurements - so this records what THIS clip actually measures and never fails a render
    /// when a statistic cannot be produced.
    /// </summary>
    Task<SceneVideoDriftMetrics> MeasureDriftAsync(
        string absolutePath,
        string? sourceFrameAbsolutePath,
        string ffmpegPath,
        CancellationToken cancellationToken = default);
}
