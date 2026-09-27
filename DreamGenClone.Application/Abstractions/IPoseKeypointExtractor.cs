namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// What a pose extraction needs. The endpoint is supplied rather than resolved here: this is the HTTP client for the
/// estimator, and which host to ask is the caller's configured decision (Model Manager), not a default buried in a
/// client. <paramref name="TimeoutSeconds"/> is likewise given, because an estimator's cost is the provider's
/// property and not something this class may guess.
/// </summary>
/// <param name="KeepFace">
/// Whether to keep the estimator's face channel. False for a body pose and true for a head-only pose: a full-body
/// skeleton's head is the COCO-18 five points and 70 face points at that scale are noise, while a head-only pose is
/// exactly the case where the face IS the pose. Required rather than defaulted, so a caller states which it wants
/// instead of inheriting one of the two by silence.
/// </param>
public sealed record PoseKeypointExtractionRequest(
    byte[] Image,
    string ProviderName,
    string BaseUrl,
    int TimeoutSeconds,
    bool KeepFace);

/// <summary>
/// What an extraction produced: the pose as an OpenPose person document, plus the provenance the plan requires of it
/// — which node ran, with which parameters, under which version of the graph. Recorded so an extracted pose can be
/// traced to the thing that measured it rather than being an anonymous set of numbers.
/// </summary>
public sealed record PoseKeypointExtractionResult(
    string PersonJson,
    string NodeName,
    string NodeSignature,
    string WorkflowVersion);

/// <summary>
/// Reads a pose out of an image. The image goes to the provider's pose estimator and comes back as COCO-18 keypoints,
/// which is what turns ANY image into a library pose — the capability the pose studio assumes elsewhere in the code
/// and did not have.
///
/// Implementations must fail loudly rather than return an empty pose: an image with no person, a person without a
/// head, and a graph the provider does not have are all distinguishable failures with their own reason codes.
/// </summary>
public interface IPoseKeypointExtractor
{
    Task<PoseKeypointExtractionResult> ExtractAsync(
        PoseKeypointExtractionRequest request, CancellationToken cancellationToken = default);
}
