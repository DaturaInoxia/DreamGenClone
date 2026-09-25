using DreamGenClone.Application.Processing;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Adapter over the Asset Manager edit stack (<see cref="ISceneAssetService"/> +
/// <see cref="ISceneAssetImageEditCompilationService"/>). Same workspace contract as the studio path, and
/// the same identity capability; what differs is only the roster SOURCE, because an asset image belongs to
/// no role-play session: its characters come from the library rather than from one scenario.
/// </summary>
public sealed class SceneAssetImageEditWorkspaceService : IImageEditWorkspaceService, IImageIdentityEditService
{
    private readonly ISceneAssetService _assets;
    private readonly ISceneAssetImageEditCompilationService _compilations;
    private readonly ITemplateService _templates;
    private readonly ICharacterImageIdentityService _identity;
    private readonly ICharacterIdentityOwnerResolver _owners;
    private readonly IDurableBackgroundJobRepository _jobs;

    public SceneAssetImageEditWorkspaceService(
        ISceneAssetService assets,
        ISceneAssetImageEditCompilationService compilations,
        ITemplateService templates,
        ICharacterImageIdentityService identity,
        ICharacterIdentityOwnerResolver owners,
        IDurableBackgroundJobRepository jobs)
    {
        _assets = assets;
        _compilations = compilations;
        _templates = templates;
        _identity = identity;
        _owners = owners;
        _jobs = jobs;
    }

    public ImageEditSubjectKind Kind => ImageEditSubjectKind.AssetImage;

    public bool SupportsIdentity => true;

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

    public async Task<ImageEditDescriptionOutcome?> GetDescriptionOutcomeAsync(
        string sessionId, CancellationToken cancellationToken = default)
    {
        // The description job's id is deterministic — "<jobType>:<dedupe key>", the same key the enqueue used — so
        // the exact row is read rather than guessed from a recent-jobs list.
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        var job = await _jobs.GetAsync(
            $"{BackgroundJobTypes.SceneAssetImageEditDescription}:{sessionId}", cancellationToken);
        return job is null ? null : ImageEditDescriptionOutcome.From(job);
    }

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
            CandidateBatchId = request.Subject.CandidateBatchId,
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

    /// <summary>
    /// The library-wide roster: every character template that can be bound. An asset image belongs to no
    /// role-play session, so there is no scenario to read characters from — the owner namespace is the whole
    /// library. Each owner is still held to the one rule that makes it bindable (exactly one approved pack,
    /// carrying approved face references), so an asset roster and a scene roster can never disagree about
    /// eligibility; only about which owners they enumerate.
    /// </summary>
    public async Task<ImageIdentityRosterResult> LoadRosterAsync(
        ImageEditSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var templates = await _templates.GetAllAsync(TemplateType.Character, cancellationToken);

        var choices = new List<ImageIdentityCharacterChoice>();
        var reasons = new List<string>();
        foreach (var template in templates)
        {
            var (choice, reason) = await ImageIdentityRosterBuilder.TryBuildChoiceAsync(
                _identity, _owners, template.Id.ToString(), template.Name, cancellationToken);
            if (choice is not null)
                choices.Add(choice);
            else if (!string.IsNullOrWhiteSpace(reason))
                reasons.Add(reason);
        }

        var ordered = choices.OrderBy(choice => choice.CharacterName, StringComparer.OrdinalIgnoreCase).ToList();
        if (ordered.Count > 0)
        {
            return new ImageIdentityRosterResult(ordered, null);
        }

        return new ImageIdentityRosterResult(
            ordered,
            reasons.Count > 0
                ? string.Join(" ", reasons.Distinct(StringComparer.Ordinal))
                : "No character currently has a single approved identity pack with approved face references.");
    }

    /// <summary>
    /// Runs the face-only identity correction on this asset image. The result is an edited child of the
    /// subject image in the same draft group, so it shows up in the attempts grid and the review deck exactly
    /// like any other edit of that asset.
    /// </summary>
    public async Task<ImageEditResultView> RunIdentityEditAsync(
        ImageEditSubject subject,
        IReadOnlyList<ImageIdentitySelection> selections,
        string editorModelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (selections.Count == 0)
        {
            throw new InvalidOperationException(
                "Select a character and an approved face for at least one detected person before running an identity edit.");
        }

        var image = await _compilations.EnqueueIdentityEditAsync(new EnqueueSceneAssetImageIdentityEditRequest
        {
            AssetId = Require(subject.AssetId, "AssetId"),
            SourceImageId = subject.ImageId,
            EditorModelId = editorModelId,
            Selections = selections
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

        // First resolution, with nothing tracked yet. The subject's candidate batch is the ownership stamp every
        // image this workspace creates carries (RunAsync passes Subject.CandidateBatchId; a crop inherits its
        // source's), so when the subject names one the result MUST come from it.
        //
        // Without that guard this fell back to "the newest image derived from the subject image", and because
        // several surfaces share one asset container, a freshly opened surface adopted whatever the container
        // produced last: the four face-angle cards recorded each other's images as their own attempts (left
        // showing right's renders, right showing left's), and re-selecting a card created another bogus attempt
        // (B-121 note 010). A subject with no batch — Panel B's step workspace — keeps the newest-derived rule.
        var images = await ListAssetImagesAsync(subject, cancellationToken);
        var latest = images
            .Where(image => string.Equals(image.SourceImageId, subject.ImageId, StringComparison.Ordinal))
            .Where(image => string.IsNullOrWhiteSpace(subject.CandidateBatchId)
                || string.Equals(image.CandidateBatchId, subject.CandidateBatchId, StringComparison.Ordinal))
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
