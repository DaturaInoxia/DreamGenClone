using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Adapter over the role-play scene-image edit stack (<see cref="ISceneImageService"/> +
/// <see cref="ISceneImageEditCompilationService"/>). This is the studio path and the only one
/// that supports scenario-character identity packs.
/// </summary>
public sealed class SceneImageEditWorkspaceService : IImageEditWorkspaceService, IImageIdentityEditService
{
    private readonly ISceneImageService _images;
    private readonly ISceneImageEditCompilationService _compilations;
    private readonly ISessionService _sessions;
    private readonly IScenarioService _scenarios;
    private readonly ICharacterImageIdentityService _identity;
    private readonly ICharacterIdentityOwnerResolver _owners;

    public SceneImageEditWorkspaceService(
        ISceneImageService images,
        ISceneImageEditCompilationService compilations,
        ISessionService sessions,
        IScenarioService scenarios,
        ICharacterImageIdentityService identity,
        ICharacterIdentityOwnerResolver owners)
    {
        _images = images;
        _compilations = compilations;
        _sessions = sessions;
        _scenarios = scenarios;
        _identity = identity;
        _owners = owners;
    }

    public ImageEditSubjectKind Kind => ImageEditSubjectKind.SceneImage;

    public bool SupportsIdentity => true;

    public async Task<ImageEditSource?> GetSourceAsync(
        ImageEditSubject subject, CancellationToken cancellationToken = default)
    {
        var record = (await ListInteractionImagesAsync(subject, cancellationToken))
            .FirstOrDefault(image => string.Equals(image.Id, subject.ImageId, StringComparison.OrdinalIgnoreCase));
        return record is null ? null : ToSource(record);
    }

    public async Task<ImageEditSessionView> OpenSessionAsync(
        ImageEditSubject subject, CancellationToken cancellationToken = default)
    {
        var session = await _compilations.CreateSessionAsync(new CreateSceneImageEditSessionRequest
        {
            SessionId = Require(subject.SessionId, "SessionId"),
            InteractionId = Require(subject.InteractionId, "InteractionId"),
            SourceImageId = subject.ImageId
        }, cancellationToken);
        return ToSession(session);
    }

    public async Task<ImageEditSessionView> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _compilations.GetSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene image edit session '{sessionId}' was not found.");
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
        var attempt = await _compilations.EnqueueCompilationAsync(new EnqueueSceneImageEditCompilationRequest
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
        var revision = await _compilations.AppendPromptRevisionAsync(new AppendSceneImageEditPromptRevisionRequest
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
        var image = await _images.EnqueueEditAsync(new SceneImageEditRequest
        {
            SessionId = Require(request.Subject.SessionId, "SessionId"),
            InteractionId = Require(request.Subject.InteractionId, "InteractionId"),
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
        var image = await _images.EnqueueCropAsync(new SceneImageCropRequest
        {
            SessionId = Require(subject.SessionId, "SessionId"),
            InteractionId = Require(subject.InteractionId, "InteractionId"),
            SourceImageId = subject.ImageId,
            Crop = crop
        }, cancellationToken);
        return ToResult(image);
    }

    public async Task<ImageEditResultView> RunEnhanceAsync(
        ImageEditSubject subject, MediaEditEnhanceOperation enhance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enhance);
        var image = await _images.EnqueueEnhanceAsync(new SceneImageEnhanceRequest
        {
            SessionId = Require(subject.SessionId, "SessionId"),
            InteractionId = Require(subject.InteractionId, "InteractionId"),
            SourceImageId = subject.ImageId,
            Enhance = enhance
        }, cancellationToken);
        return ToResult(image);
    }

    public async Task<ImageEditResultView?> ResolveResultAsync(
        ImageEditSubject subject, string? sessionId, string? trackedResultId, CancellationToken cancellationToken = default)
    {
        var images = await ListInteractionImagesAsync(subject, cancellationToken);
        if (!string.IsNullOrWhiteSpace(trackedResultId))
        {
            var tracked = images.FirstOrDefault(image => string.Equals(image.Id, trackedResultId, StringComparison.Ordinal));
            if (tracked is not null)
                return ToResult(tracked);
        }

        var latest = string.IsNullOrWhiteSpace(sessionId)
            ? null
            : images
                .Where(image => string.Equals(image.EditSessionId, sessionId, StringComparison.Ordinal))
                .OrderByDescending(image => image.CreatedUtc)
                .FirstOrDefault();
        return latest is null ? null : ToResult(latest);
    }

    public async Task<IReadOnlyList<ImageEditLineageItem>> ListLineageAsync(
        ImageEditSubject subject, CancellationToken cancellationToken = default)
    {
        var images = await ListInteractionImagesAsync(subject, cancellationToken);
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
                image.Status == SceneImageStatus.Complete,
                image.Operation == SceneImageOperation.Edit,
                image.PromptSnapshot,
                image.CreatedUtc))
            .ToList();
    }

    public async Task<ImageIdentityRosterResult> LoadRosterAsync(
        ImageEditSubject subject, CancellationToken cancellationToken = default)
    {
        var session = string.IsNullOrWhiteSpace(subject.SessionId)
            ? null
            : await _sessions.LoadRolePlaySessionAsync(subject.SessionId);
        if (session is null || string.IsNullOrWhiteSpace(session.ScenarioId))
            return new ImageIdentityRosterResult([], "This role-play session has no scenario characters to bind identity packs to.");

        var scenario = await _scenarios.GetScenarioAsync(session.ScenarioId);
        if (scenario?.Characters is not { Count: > 0 } characters)
            return new ImageIdentityRosterResult([], "This scenario defines no characters, so no identity packs can be applied.");

        var choices = new List<ImageIdentityCharacterChoice>();
        var reasons = new List<string>();
        foreach (var character in characters)
        {
            if (string.IsNullOrWhiteSpace(character.Id))
                continue;

            // The scenario character is a PROJECTION of its template (B-127), so the builder resolves the owner
            // before reading packs. Passing the raw scenario id here is what produced "no scenario characters
            // currently have an approved identity pack" for characters whose packs exist under their template.
            var (choice, reason) = await ImageIdentityRosterBuilder.TryBuildChoiceAsync(
                _identity, _owners, character.Id, character.Name ?? string.Empty, cancellationToken);
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

        // Say WHICH rule each character failed — including the resolver's own "link it to a template" refusal —
        // so an empty roster can be acted on instead of only stating that it is empty.
        return new ImageIdentityRosterResult(
            ordered,
            reasons.Count > 0
                ? string.Join(" ", reasons.Distinct(StringComparer.Ordinal))
                : "No scenario characters currently have an approved identity pack with approved face references.");
    }

    public async Task<ImageEditResultView> RunIdentityEditAsync(
        ImageEditSubject subject,
        IReadOnlyList<ImageIdentitySelection> selections,
        string editorModelId,
        CancellationToken cancellationToken = default)
    {
        if (selections.Count == 0)
        {
            throw new InvalidOperationException(
                "Select a character and an approved face for at least one detected person before running an identity edit.");
        }

        var image = await _images.EnqueueEditorIdentityAsync(new SceneImageEditorIdentityRequest
        {
            SessionId = Require(subject.SessionId, "SessionId"),
            InteractionId = Require(subject.InteractionId, "InteractionId"),
            SourceImageId = subject.ImageId,
            // The model the editor form chose. Resolution happens once, from this id, at dispatch.
            EditorModelId = (editorModelId ?? string.Empty).Trim(),
            Selections = selections.Select(selection => new SceneImageEditorIdentitySelection
            {
                TargetKey = selection.TargetKey,
                VisibleLocator = selection.VisibleLocator,
                Region = selection.Region,
                CharacterId = selection.CharacterId,
                CharacterName = selection.CharacterName,
                ReferenceAssetId = selection.ReferenceAssetId
            }).ToList()
        }, cancellationToken);
        return ToResult(image);
    }

    private async Task<IReadOnlyList<SceneImageRecord>> ListInteractionImagesAsync(
        ImageEditSubject subject, CancellationToken cancellationToken)
        => await _images.ListImagesByInteractionAsync(
            Require(subject.SessionId, "SessionId"),
            Require(subject.InteractionId, "InteractionId"),
            cancellationToken);

    private static ImageEditSource ToSource(SceneImageRecord record)
        => new(
            record.Id,
            record.FileRelativePath ?? string.Empty,
            record.ImageSize,
            record.Status.ToString(),
            record.Status == SceneImageStatus.Complete && !string.IsNullOrWhiteSpace(record.FileRelativePath));

    private static ImageEditSessionView ToSession(SceneImageEditSession session)
        => new(
            session.Id,
            session.Status.ToString(),
            session.Status == SceneImageEditSessionStatus.Completed,
            session.DescriptionText,
            session.SourceImageSha256);

    private static ImageEditAttemptView ToAttempt(SceneImageEditCompilationAttempt attempt)
        => new(
            attempt.Id,
            attempt.Status == SceneImageEditCompilationAttemptStatus.ClarificationRequired
                ? "Needs clarification"
                : attempt.Status.ToString(),
            attempt.ParsedResultJson,
            attempt.Error,
            attempt.Status is SceneImageEditCompilationAttemptStatus.Pending or SceneImageEditCompilationAttemptStatus.Compiling,
            attempt.Status is SceneImageEditCompilationAttemptStatus.Invalid or SceneImageEditCompilationAttemptStatus.Failed);

    private static ImageEditResultView ToResult(SceneImageRecord image)
        => new(
            image.Id,
            image.FileRelativePath,
            image.Status.ToString(),
            image.Status == SceneImageStatus.Complete && !string.IsNullOrWhiteSpace(image.FileRelativePath),
            image.Status is SceneImageStatus.Pending or SceneImageStatus.Generating,
            image.ErrorMessage);

    private static string Require(string? value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"A scene image edit subject requires '{name}'.")
            : value;
}
