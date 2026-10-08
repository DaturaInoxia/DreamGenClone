namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Measured evidence about a stored clip. Every field is MEASURED from the file, never assumed: a clip whose audio
/// stream is missing or silent must be a visible failure in the queue rather than a review-time surprise.
/// </summary>
/// <param name="VideoStreamPresent">A video stream was found.</param>
/// <param name="AudioStreamPresent">An audio stream was found.</param>
/// <param name="DurationSeconds">Container duration, when it could be read.</param>
/// <param name="IntegratedLoudnessLufs">Integrated loudness (EBU R128), when it could be measured.</param>
/// <param name="Notes">Human-readable warnings (for example "audio stream present but carries no signal").</param>
public sealed record SceneVideoAudioEvidence(
    bool VideoStreamPresent,
    bool AudioStreamPresent,
    double? DurationSeconds,
    double? IntegratedLoudnessLufs,
    string? Notes);
