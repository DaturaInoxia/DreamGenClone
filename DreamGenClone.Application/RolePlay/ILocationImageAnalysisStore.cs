using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// Writes what a vision model found in a LOCATION image (B-160).
/// </summary>
/// <remarks>
/// <para>
/// A contract of its own rather than three more members on <see cref="ISceneAssetRepository"/>, for one structural
/// reason: the analysis is written by its own job and by nothing else, so it must be impossible for an ordinary image
/// save to overwrite it. Keeping the write behind this interface means the completion path, the approval path and every
/// other writer of an image row cannot carry a stale analysis forward by accident — they never touch these columns at
/// all.
/// </para>
///
/// <para>
/// The stored checksum is the analysed version's, which is what lets a later reader answer "is this still about the
/// picture I am looking at?" without trusting a timestamp.
/// </para>
/// </remarks>
public interface ILocationImageAnalysisStore
{
    /// <summary>
    /// Records the analysis of one location image, or clears it when <paramref name="analysis"/> is null.
    /// </summary>
    /// <param name="imageId">The location image the analysis is about.</param>
    /// <param name="analysis">What was found, or null to record that the image is no longer analysed.</param>
    Task SaveAsync(string imageId, LocationImageAnalysis? analysis, CancellationToken cancellationToken = default);
}
