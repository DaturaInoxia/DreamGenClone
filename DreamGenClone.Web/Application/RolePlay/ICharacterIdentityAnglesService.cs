using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public interface ICharacterIdentityAnglesService
{
    Task<IReadOnlyList<CharacterIdentityAngleRecord>> ListAsync(string buildId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterIdentityAngleAttempt>> ListAttemptsAsync(string buildId, CharacterIdentityAngleView view, CancellationToken cancellationToken = default);

    Task<CharacterIdentityAngleRecord> RunAsync(
        string buildId,
        CharacterIdentityAngleView view,
        string modelId,
        string? promptOverride = null,
        CancellationToken cancellationToken = default);

    Task<CharacterIdentityAngleRecord> PrepareAsync(
        string buildId,
        CharacterIdentityAngleView view,
        string modelId,
        string? promptOverride = null,
        CancellationToken cancellationToken = default);

    Task<CharacterIdentityAngleRecord> UploadAsync(
        string buildId,
        CharacterIdentityAngleView view,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<CharacterIdentityAngleRecord> RecordResultAsync(
        string buildId,
        CharacterIdentityAngleView view,
        string outputArtifactId,
        string? promptText = null,
        CancellationToken cancellationToken = default);

    Task<CharacterIdentityAngleRecord> AcceptAsync(
        string buildId,
        CharacterIdentityAngleView view,
        bool manualConfirmed = false,
        CancellationToken cancellationToken = default);

    Task<CharacterIdentityAngleRecord> AcceptAttemptAsync(
        string buildId,
        CharacterIdentityAngleView view,
        string attemptId,
        bool manualConfirmed = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts one image of this view's flow as the angle — including an image the edit / crop / enhance chain
    /// produced from an attempt, which the attempt list alone cannot select.
    /// </summary>
    Task<CharacterIdentityAngleRecord> AcceptCandidateAsync(
        string buildId,
        CharacterIdentityAngleView view,
        string imageId,
        bool manualConfirmed = false,
        CancellationToken cancellationToken = default);

    Task DeleteAttemptAsync(
        string buildId,
        CharacterIdentityAngleView view,
        string attemptId,
        CancellationToken cancellationToken = default);

    Task RecordOverrideAsync(string buildId, CharacterIdentityAngleView view, string attemptId, string reason, string author, CancellationToken cancellationToken = default);
}