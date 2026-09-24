namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// The ONE edit path. Every surface that edits an existing image (Asset Studio, the role-play
/// Scene Image Studio editor, and the Character Identity steps) drives the same
/// <c>ImageEditWorkspace</c> component through this contract, so a fix or feature lands once.
/// Implementations are per-store adapters; the stores themselves are collapsed in B-124/B124-012.
/// </summary>
public interface IImageEditWorkspaceService
{
    ImageEditSubjectKind Kind { get; }

    /// <summary>True when this store can bind scenario-character identity packs to edited images.</summary>
    bool SupportsIdentity { get; }

    Task<ImageEditSource?> GetSourceAsync(ImageEditSubject subject, CancellationToken cancellationToken = default);

    Task<ImageEditSessionView> OpenSessionAsync(ImageEditSubject subject, CancellationToken cancellationToken = default);

    Task<ImageEditSessionView> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task ReanalyzeAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<ImageEditAttemptView?> GetLatestAttemptAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<ImageEditAttemptView> PrepareAsync(
        string sessionId,
        string rawIntent,
        IReadOnlyList<string> clarificationHistory,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageEditRevisionView>> ListRevisionsAsync(
        string compilationAttemptId, CancellationToken cancellationToken = default);

    Task<ImageEditRevisionView> AppendRevisionAsync(
        string sessionId,
        string compilationAttemptId,
        string prompt,
        CancellationToken cancellationToken = default);

    Task<ImageEditResultView> RunAsync(ImageEditRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a deterministic crop of this subject's stored image into a new derived image. It is an
    /// operation of the same pipeline as <see cref="RunAsync"/>, but there is no compiled prompt and no
    /// editor model: the caller supplies the finished crop parameters, including the measured face when
    /// the head-aware mode is used.
    /// </summary>
    Task<ImageEditResultView> RunCropAsync(
        ImageEditSubject subject, MediaEditCropOperation crop, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs an enhance of this subject's stored image into a new derived image: the configured upscaler
    /// ran on it and the result was scaled back to the configured long edge. It is an operation of the
    /// same pipeline as <see cref="RunCropAsync"/> and, like a crop, involves no editor model or prompt.
    /// </summary>
    Task<ImageEditResultView> RunEnhanceAsync(
        ImageEditSubject subject, MediaEditEnhanceOperation enhance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the rendered result for the current session, preferring the tracked result id so an
    /// in-flight image stays visible while it progresses.
    /// </summary>
    Task<ImageEditResultView?> ResolveResultAsync(
        ImageEditSubject subject,
        string? sessionId,
        string? trackedResultId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageEditLineageItem>> ListLineageAsync(
        ImageEditSubject subject, CancellationToken cancellationToken = default);
}

/// <summary>
/// The optional identity capability. Kept separate from the edit contract so a store without identity
/// packs simply does not implement it and the workspace hides the capability.
///
/// It takes the whole <see cref="ImageEditSubject"/> rather than only the field one store happens to
/// need, because the roster SOURCE is store-specific: a scene image reads the characters of its session's
/// scenario, while an asset image has no session and reads the characters of the library. Only the adapter
/// knows which of its subject's fields name that source.
/// </summary>
public interface IImageIdentityEditService
{
    ImageEditSubjectKind Kind { get; }

    Task<ImageIdentityRosterResult> LoadRosterAsync(
        ImageEditSubject subject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the face-only identity correction. <paramref name="editorModelId"/> is the model the editor
    /// form selected — the same decision every other run of that form carries — so the identity run can
    /// never resolve an editor model the user did not choose.
    /// </summary>
    Task<ImageEditResultView> RunIdentityEditAsync(
        ImageEditSubject subject,
        IReadOnlyList<ImageIdentitySelection> selections,
        string editorModelId,
        CancellationToken cancellationToken = default);
}

/// <summary>Selects the adapter for a subject kind. Missing adapters fail fast — never a default.</summary>
public sealed class ImageEditWorkspaceServiceResolver
{
    private readonly IReadOnlyList<IImageEditWorkspaceService> _services;
    private readonly IReadOnlyList<IImageIdentityEditService> _identityServices;

    public ImageEditWorkspaceServiceResolver(
        IEnumerable<IImageEditWorkspaceService> services,
        IEnumerable<IImageIdentityEditService> identityServices)
    {
        _services = services.ToList();
        _identityServices = identityServices.ToList();
    }

    public IImageEditWorkspaceService Resolve(ImageEditSubjectKind kind)
        => _services.FirstOrDefault(service => service.Kind == kind)
            ?? throw new InvalidOperationException(
                $"No image edit workspace service is registered for subject kind '{kind}'.");

    public IImageIdentityEditService? ResolveIdentity(ImageEditSubjectKind kind)
        => _identityServices.FirstOrDefault(service => service.Kind == kind);
}
