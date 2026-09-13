using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Adapter over the Asset Manager edit stack (<see cref="ISceneAssetService"/> +
/// <see cref="ISceneAssetImageEditCompilationService"/>). Same workspace contract as the studio path,
/// minus identity packs, which only exist for scenario characters.
/// </summary>
public sealed class SceneAssetImageEditWorkspaceService : IImageEditWorkspaceService
{
    private readonly ISceneAssetService _assets;
    private readonly ISceneAssetImageEditCompilationService _compilations;

    public SceneAssetImageEditWorkspaceService(
        ISceneAssetService assets,
        ISceneAssetImageEditCompilationService compilations)
    {
        _assets = assets;
        _compilations = compilations;
    }

    public ImageEditSubjectKind Kind => ImageEditSubjectKind.AssetImage;

    public bool SupportsIdentity => false;

    public async Task<ImageEditSource?> GetSourceAsync(
        ImageEditSubject subject, CancellationToken cancellationToken = default)
    {
        var image = await _assets.GetImageAsync(subject.ImageId, cancellationToken);
        return image is null ? null : ToSource(image);
    }

    public async Task<ImageEditSessionView> OpenSessionAsync(
        ImageEditSubject subject, CancellationToken cancellationToken = default)
    {
        var session = await _compilations.CreateSessionAsync(new CreateSceneAssetImageEditSessionRequest
        {
            AssetId = Require(subject.AssetId, "AssetId"),
            SourceImageId = subject.ImageId
        }, cancellationToken);
        return ToSession(session);
    }

    public async Task<ImageEditSessionView> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _compilations.GetSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Asset image edit session '{sessionId}' was not found.");
        return ToSession(session);
    }

    public Task ReanalyzeAsync(string sessionId, CancellationToken cancellationToken = default)
        => _compilations.EnqueueDescriptionAsync(sessionId, force: true, cancellationToken);

    public async Task<ImageEditAttemptView?> GetLatestAttemptAsync(
        string sessionId, CancellationToken cancellationToken = default)
    {
        var attempt = await _compilations.GetLatestAttemptAsync(sessionId, cancellationToken);
        return attempt is null ? null : ToAttempt(attempt);
    }

    public async Task<ImageEditAttemptView> PrepareAsync(
        string sessionId, string rawIntent, IReadOnlyList<string> clarificationHistory, CancellationToken cancellationToken = default)
    {
        var attempt = await _compilations.EnqueueCompilationAsync(new EnqueueSceneAssetImageEditCompilationRequest
        {
            EditSessionId = sessionId,
            RawIntent = rawIntent,
            ClarificationHistory = clarificationHistory
        }, cancellationToken);
        return ToAttempt(attempt);
    }

    public async Task<IReadOnlyList<ImageEditRevisionView>> ListRevisionsAsync(
        string compilationAttemptId, CancellationToken cancellationToken = default)
        => (await _compilations.ListRevisionsAsync(compilationAttemptId, cancellationToken))
            .Select(revision => new ImageEditRevisionView(
                revision.Id,
                revision.Ordinal,
                revision.Prompt,
                revision.PromptSha256,
                revision.RevisionKind == SceneImageEditPromptRevisionKind.UserEdited))
            .ToList();

    public async Task<ImageEditRevisionView> AppendRevisionAsync(
        string sessionId, string compilationAttemptId, string prompt, CancellationToken cancellationToken = default)
    {
        var revision = await _compilations.AppendPromptRevisionAsync(new AppendSceneAssetImageEditPromptRevisionRequest
        {
            EditSessionId = sessionId,
            CompilationAttemptId = compilationAttemptId,
            Prompt = prompt
        }, cancellationToken);
        return new ImageEditRevisionView(
            revision.Id,
            revision.Ordinal,
            revision.Prompt,
            revision.PromptSha256,
            revision.RevisionKind == SceneImageEditPromptRevisionKind.UserEdited);
    }

    public async Task<ImageEditResultView> RunAsync(
        ImageEditRunRequest request, CancellationToken cancellationToken = default)
    {
        var image = await _compilations.EnqueueEditAsync(new EnqueueSceneAssetImageEditRequest
        {
            AssetId = Require(request.Subject.AssetId, "AssetId"),
            SourceImageId = request.Subject.ImageId,
            EditSessionId = request.SessionId,
            CompilationAttemptId = request.AttemptId,
            PromptRevisionId = request.RevisionId,
            SourceImageSha256 = request.SourceImageSha256,
            PromptSha256 = request.PromptSha256,
            EditorModelId = request.EditorModelId,
            ReferenceApplications = request.ReferenceApplications.ToList()
        }, cancellationToken);
        return ToResult(image);
    }

    public async Task<ImageEditResultView> RunCropAsync(
        ImageEditSubject subject, MediaEditCropOperation crop, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crop);
        var image = await _compilations.EnqueueCropAsync(new EnqueueSceneAssetImageCropRequest
        {
            AssetId = Require(subject.AssetId, "AssetId"),
            SourceImageId = subject.ImageId,
            Crop = crop
        }, cancellationToken);
        return ToResult(image);
    }

    public async Task<ImageEditResultView> RunEnhanceAsync(
        ImageEditSubject subject, MediaEditEnhanceOperation enhance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enhance);
        var image = await _compilations.EnqueueEnhanceAsync(new EnqueueSceneAssetImageEnhanceRequest
        {
            AssetId = Require(subject.AssetId, "AssetId"),
            SourceImageId = subject.ImageId,
            Enhance = enhance
        }, cancellationToken);
        return ToResult(image);
    }

    public async Task<ImageEditResultView?> ResolveResultAsync(
        ImageEditSubject subject, string? sessionId, string? trackedResultId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(trackedResultId))
        {
            var tracked = await _assets.GetImageAsync(trackedResultId, cancellationToken);
            if (tracked is not null)
                return ToResult(tracked);
        }

        // Asset images are not stamped with the edit session, so the newest derived image of the
        // source is the session's result.
        var images = await ListAssetImagesAsync(subject, cancellationToken);
        var latest = images
            .Where(image => string.Equals(image.SourceImageId, subject.ImageId, StringComparison.Ordinal))
            .OrderByDescending(image => image.CreatedUtc)
            .FirstOrDefault();
        return latest is null ? null : ToResult(latest);
    }

    public async Task<IReadOnlyList<ImageEditLineageItem>> ListLineageAsync(
        ImageEditSubject subject, CancellationToken cancellationToken = default)
    {
        var images = await ListAssetImagesAsync(subject, cancellationToken);
        var selected = images.FirstOrDefault(image => string.Equals(image.Id, subject.ImageId, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
            return [];

        var byId = images.ToDictionary(image => image.Id, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = selected;
        while (!string.IsNullOrWhiteSpace(root.SourceImageId)
            && visited.Add(root.Id)
            && byId.TryGetValue(root.SourceImageId, out var parent))
        {
            root = parent;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.Id };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var image in images)
            {
                if (!string.IsNullOrWhiteSpace(image.SourceImageId)
                    && ids.Contains(image.SourceImageId)
                    && ids.Add(image.Id))
                {
                    changed = true;
                }
            }
        }

        return images
            .Where(image => ids.Contains(image.Id))
            .OrderBy(image => image.CreatedUtc)
            .Select(image => new ImageEditLineageItem(
                image.Id,
                image.FileRelativePath ?? string.Empty,
                image.Status.ToString(),
                image.Status == SceneAssetStatus.Complete && !string.IsNullOrWhiteSpace(image.FileRelativePath),
                !string.IsNullOrWhiteSpace(image.SourceImageId),
                string.IsNullOrWhiteSpace(image.Prompt) ? null : image.Prompt,
                image.CreatedUtc))
            .ToList();
    }

    private async Task<IReadOnlyList<SceneAssetImage>> ListAssetImagesAsync(
        ImageEditSubject subject, CancellationToken cancellationToken)
    {
        var assetId = subject.AssetId;
        if (string.IsNullOrWhiteSpace(assetId))
        {
            var image = await _assets.GetImageAsync(subject.ImageId, cancellationToken)
                ?? throw new InvalidOperationException($"Asset image '{subject.ImageId}' was not found.");
            assetId = image.AssetId;
        }

        return await _assets.ListImagesAsync(assetId, cancellationToken);
    }

    private static ImageEditSource ToSource(SceneAssetImage image)
        => new(
            image.Id,
            image.FileRelativePath ?? string.Empty,
            image.Width.HasValue && image.Height.HasValue ? $"{image.Width}x{image.Height}" : null,
            image.Status.ToString(),
            image.Status == SceneAssetStatus.Complete && !string.IsNullOrWhiteSpace(image.FileRelativePath));

    private static ImageEditSessionView ToSession(SceneAssetImageEditSession session)
        => new(
            session.Id,
            session.Status.ToString(),
            session.Status == SceneAssetImageEditSessionStatus.Completed,
            session.DescriptionText,
            session.SourceImageSha256);

    private static ImageEditAttemptView ToAttempt(SceneAssetImageEditCompilationAttempt attempt)
        => new(
            attempt.Id,
            attempt.Status == SceneImageEditCompilationAttemptStatus.ClarificationRequired
                ? "Needs clarification"
                : attempt.Status.ToString(),
            attempt.ParsedResultJson,
            attempt.Error,
            attempt.Status is SceneImageEditCompilationAttemptStatus.Pending or SceneImageEditCompilationAttemptStatus.Compiling,
            attempt.Status is SceneImageEditCompilationAttemptStatus.Invalid or SceneImageEditCompilationAttemptStatus.Failed);

    private static ImageEditResultView ToResult(SceneAssetImage image)
        => new(
            image.Id,
            image.FileRelativePath,
            image.Status.ToString(),
            image.Status == SceneAssetStatus.Complete && !string.IsNullOrWhiteSpace(image.FileRelativePath),
            image.Status == SceneAssetStatus.Pending,
            image.ErrorMessage);

    private static string Require(string? value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"An asset image edit subject requires '{name}'.")
            : value;
}
