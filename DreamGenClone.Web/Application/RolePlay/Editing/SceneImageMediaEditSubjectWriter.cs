using System.Security.Cryptography;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Role-play scene subjects, covering all three stage paths the scene editing handler owned:
/// the plain compiled edit, Identity (persisted face bindings) and Finish (adult-content policy
/// gate). The write block exists once here instead of once per stage.
/// </summary>
public sealed class SceneImageMediaEditSubjectWriter : IMediaEditSubjectWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneImageRepository _images;
    private readonly ISceneImageEditRepository _edits;
    private readonly ISceneImageStorageService _storage;
    private readonly ICharacterImageAssetStorageService? _identityStorage;
    private readonly MediaEditReferenceResolver? _references;
    private readonly ILogger<SceneImageMediaEditSubjectWriter> _logger;

    public SceneImageMediaEditSubjectWriter(
        ISceneImageRepository images,
        ISceneImageEditRepository edits,
        ISceneImageStorageService storage,
        ILogger<SceneImageMediaEditSubjectWriter> logger,
        MediaEditReferenceResolver? references = null,
        ICharacterImageAssetStorageService? identityStorage = null)
    {
        _images = images;
        _edits = edits;
        _storage = storage;
        _logger = logger;
        _references = references;
        _identityStorage = identityStorage;
    }

    public MediaEditSubjectKind Kind => MediaEditSubjectKind.SceneImage;

    public async Task<MediaEditRunPlan?> PrepareAsync(
        MediaEditRunContext context, CancellationToken cancellationToken = default)
    {
        var image = await _images.GetImageAsync(context.ImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene image edit record '{context.ImageId}' was not found.");

        // A redelivered job must not edit an image that already finished (or was cancelled).
        if (image.Status is SceneImageStatus.Complete or SceneImageStatus.Cancelled)
            return null;

        // An operation is not a render and has no production stage of its own: it is resolved on its own
        // path so it never falls into a stage's compiler-provenance validation.
        if (context.Operation.Kind != MediaEditOperationKind.Edit)
            return await PrepareOperationAsync(image, context, cancellationToken);

        return image.ProductionStage switch
        {
            SceneImageProductionStage.Identity => await PrepareIdentityAsync(image, context, cancellationToken),
            SceneImageProductionStage.Finish => await PrepareFinishAsync(image, cancellationToken),
            _ => await PrepareCompiledEditAsync(image, context, cancellationToken)
        };
    }

    /// <summary>
    /// Claims the row an edit run is about to execute. The completion transition
    /// (<see cref="ISceneImageRepository.TryCompleteImageAsync"/>) only matches 'Generating', so an
    /// unclaimed edit silently loses its result — which is exactly what the studio showed as an edit
    /// stuck pending forever.
    /// </summary>
    public async Task<bool> ClaimAsync(
        MediaEditRunContext context, CancellationToken cancellationToken = default)
    {
        // An operation is never claimed: it is finished by the same job that picked it up and its
        // completion accepts a row that is still 'Pending' (TryCompleteOperationImageAsync).
        if (context.Operation.Kind != MediaEditOperationKind.Edit)
            return true;

        if (await _images.TryClaimImageAsync(context.ImageId, DateTime.UtcNow, cancellationToken))
            return true;

        // The claim matched nothing: either the row is terminal (nothing left to run) or an earlier
        // delivery of this job claimed it and died before completing, in which case this delivery must
        // finish the work rather than abandon the row in 'Generating'.
        var image = await _images.GetImageAsync(context.ImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene image edit record '{context.ImageId}' was not found.");
        return image.Status == SceneImageStatus.Generating;
    }

    private async Task<MediaEditRunPlan> PrepareCompiledEditAsync(
        SceneImageRecord image, MediaEditRunContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ExplicitEditorModelId))
            throw new InvalidOperationException("Scene image edit jobs require an exact selected editor model id.");
        if (string.IsNullOrWhiteSpace(image.EditSessionId)
            || string.IsNullOrWhiteSpace(image.EditCompilationAttemptId)
            || string.IsNullOrWhiteSpace(image.EditPromptRevisionId)
            || string.IsNullOrWhiteSpace(image.EditCompilerProvenanceJson))
        {
            throw new InvalidOperationException("Scene image editing requires exact compiler provenance.");
        }

        using var provenance = JsonDocument.Parse(image.EditCompilerProvenanceJson);
        var sourceSha256 = provenance.RootElement.GetProperty("sourceImageSha256").GetString()
            ?? throw new InvalidOperationException("Edit provenance is missing the source checksum.");
        var promptSha256 = provenance.RootElement.GetProperty("promptSha256").GetString()
            ?? throw new InvalidOperationException("Edit provenance is missing the prompt checksum.");
        var revision = await _edits.GetExecutableRevisionAsync(
            image.EditSessionId, image.EditCompilationAttemptId, image.EditPromptRevisionId,
            sourceSha256, promptSha256, cancellationToken);
        if (!string.Equals(revision.Prompt, image.PromptSnapshot, StringComparison.Ordinal))
            throw new InvalidOperationException("The queued edit prompt does not match the accepted prompt revision.");

        var source = await RequireSourceAsync(image, cancellationToken);
        var (references, prompt) = await BuildAssetReferencesAsync(image, cancellationToken);

        return new MediaEditRunPlan(
            image.Id,
            source.Id,
            token => _storage.OpenReadAsync(SourcePath(source), token),
            sourceSha256,
            MediaEditOperation.ForEdit,
            Prompt: prompt,
            References: references,
            Editor: new MediaEditEditorResolution(context.ExplicitEditorModelId, RequiresAdultContentPolicy: false),
            LogScope: $"SessionId={image.SessionId}, InteractionId={image.InteractionId}");
    }

    /// <summary>
    /// Prepares a deterministic operation on a scene image. Only the queued row, its complete stored
    /// source and the source's stored checksum are needed: the operation was never compiled, so there is
    /// no compiler artifact to validate.
    /// </summary>
    private async Task<MediaEditRunPlan?> PrepareOperationAsync(
        SceneImageRecord image, MediaEditRunContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(image.SourceImageId))
            throw new InvalidOperationException(
                $"A scene image {context.Operation.Kind} requires the queued image to name its source image.");

        var source = await RequireSourceAsync(image, cancellationToken);
        if (string.IsNullOrWhiteSpace(source.Sha256))
            throw new InvalidOperationException("The source scene image has no stored checksum.");

        return new MediaEditRunPlan(
            image.Id,
            source.Id,
            token => _storage.OpenReadAsync(SourcePath(source), token),
            source.Sha256,
            context.Operation,
            LogScope: $"SessionId={image.SessionId}, InteractionId={image.InteractionId}");
    }

    private async Task<MediaEditRunPlan> PrepareIdentityAsync(
        SceneImageRecord image, MediaEditRunContext context, CancellationToken cancellationToken)
    {
        var identityStorage = _identityStorage
            ?? throw new InvalidOperationException("Identity image editing requires identity asset storage.");
        if (string.IsNullOrWhiteSpace(image.SourceImageId) || string.IsNullOrWhiteSpace(image.IdentityReferenceBindingsJson))
            throw new InvalidOperationException("Identity image editing requires a source image and persisted reference bindings.");
        if (string.IsNullOrWhiteSpace(context.ExplicitEditorModelId))
            throw new InvalidOperationException("Identity image editing requires the editor model chosen in the editor form.");

        var source = await RequireSourceAsync(image, cancellationToken);
        var bindings = JsonSerializer.Deserialize<IReadOnlyList<IdentityBinding>>(
            image.IdentityReferenceBindingsJson, JsonOptions)
            ?? throw new InvalidOperationException("Identity reference bindings are invalid.");
        if (bindings.Count == 0
            || bindings.Select(binding => binding.Ordinal).SequenceEqual(bindings.Select(binding => binding.Ordinal).OrderBy(x => x)) is false)
        {
            throw new InvalidOperationException("Identity reference bindings must be non-empty and ordinally ordered.");
        }

        var references = new List<MediaEditReference>(bindings.Count);
        foreach (var binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.FileRelativePath) || string.IsNullOrWhiteSpace(binding.Sha256))
                throw new InvalidOperationException("Identity reference binding is missing exact asset metadata.");

            var relativePath = binding.FileRelativePath;
            references.Add(new MediaEditReference(
                binding.Ordinal,
                $"selected face identity reference for {binding.CharacterName} (CharacterFace:{binding.CharacterId})",
                $"{binding.CharacterId}.png",
                binding.Sha256,
                token => identityStorage.OpenReadAsync(relativePath, token)));
        }

        return new MediaEditRunPlan(
            image.Id,
            source.Id,
            token => _storage.OpenReadAsync(SourcePath(source), token),
            source.Sha256,
            MediaEditOperation.ForEdit,
            Prompt: image.PromptSnapshot,
            References: references,
            Editor: new MediaEditEditorResolution(context.ExplicitEditorModelId, RequiresAdultContentPolicy: false),
            LogScope: $"SessionId={image.SessionId}, InteractionId={image.InteractionId}, Stage=Identity");
    }

    private async Task<MediaEditRunPlan> PrepareFinishAsync(
        SceneImageRecord image, CancellationToken cancellationToken)
    {
        var source = await RequireSourceAsync(image, cancellationToken);
        var requestAdultContent = false;
        if (!string.IsNullOrWhiteSpace(image.EditIntentSnapshot))
        {
            using var intent = JsonDocument.Parse(image.EditIntentSnapshot);
            requestAdultContent = intent.RootElement.TryGetProperty("RequestAdultContent", out var value)
                && value.ValueKind == JsonValueKind.True;
        }

        var (references, prompt) = await BuildAssetReferencesAsync(image, cancellationToken);
        return new MediaEditRunPlan(
            image.Id,
            source.Id,
            token => _storage.OpenReadAsync(SourcePath(source), token),
            source.Sha256,
            MediaEditOperation.ForEdit,
            Prompt: prompt,
            References: references,
            Editor: new MediaEditEditorResolution(ExplicitModelId: null, RequiresAdultContentPolicy: requestAdultContent),
            LogScope: $"SessionId={image.SessionId}, InteractionId={image.InteractionId}, Stage=Finish");
    }

    /// <summary>
    /// Applied reference bindings, plus the instruction the model needs when references accompany the
    /// source. Empty bindings mean a plain text edit with the stored prompt untouched.
    /// </summary>
    private async Task<(IReadOnlyList<MediaEditReference> References, string Prompt)> BuildAssetReferencesAsync(
        SceneImageRecord image, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(image.AppliedReferenceBindingsJson))
            return ([], image.PromptSnapshot);

        var applications = JsonSerializer.Deserialize<IReadOnlyList<ReferenceApplicationSelection>>(
            image.AppliedReferenceBindingsJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image edit reference applications are invalid.");

        var resolver = _references
            ?? throw new InvalidOperationException("Scene image edit reference application requires the reference resolver.");
        var references = await resolver.ResolveAsync(
            null, applications, qualifiedStrategy: "NativeMultiReference", cancellationToken);
        if (references.Count == 0)
            return ([], image.PromptSnapshot);

        var used = applications
            .Where(application => application.UsesReference
                && !string.Equals(application.Strategy, "TextOnly", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return (references, MediaEditReferenceResolver.BuildReferenceAwareInstruction(image.PromptSnapshot, used));
    }

    private async Task<SceneImageRecord> RequireSourceAsync(
        SceneImageRecord image, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(image.SourceImageId))
            throw new InvalidOperationException($"A source image is required for stage {image.ProductionStage}.");

        var source = await _images.GetImageAsync(image.SourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene image '{image.SourceImageId}' was not found.");
        if (source.Status != SceneImageStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath))
            throw new InvalidOperationException("The source scene image must be complete and stored.");

        return source;
    }

    private static string SourcePath(SceneImageRecord source)
        => source.FileRelativePath ?? throw new InvalidOperationException("The source scene image has no stored path.");

    public async Task CompleteAsync(
        MediaEditRunPlan plan, MediaEditRunOutput output, CancellationToken cancellationToken = default)
    {
        var image = await _images.GetImageAsync(plan.ImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene image edit record '{plan.ImageId}' was not found.");

        await using var content = new MemoryStream(output.Bytes);
        image.FileRelativePath = await _storage.SaveAsync(image.SessionId, $"{image.Id}.png", content, cancellationToken);

        if (output.Operation != MediaEditOperationKind.Edit)
        {
            // A crop is not a render: the row must not claim a model, provider or content policy that
            // never took part, so those fields are left exactly as the queued row had them.
        }
        else
        {
            image.ModelIdentifier = output.ModelIdentifier
                ?? throw new InvalidOperationException("An edit run must report the model identifier it ran on.");
            image.ProviderName = output.ProviderName
                ?? throw new InvalidOperationException("An edit run must report the provider it ran on.");
            image.ContentPolicy = output.ContentPolicy
                ?? throw new InvalidOperationException("An edit run must report the content policy it ran under.");
        }

        image.Sha256 = Convert.ToHexString(SHA256.HashData(output.Bytes));
        // The row describes the file that now exists: an operation changes the size (a crop trims, an
        // enhance resamples), so the source's size was only ever a placeholder for these rows.
        image.ImageSize = MediaEditProducedImage.SizeOf(output.Bytes);
        image.Status = SceneImageStatus.Complete;
        image.ErrorMessage = null;
        image.CompletedUtc = DateTime.UtcNow;
        image.UpdatedUtc = DateTime.UtcNow;

        // An operation is never claimed, so it completes a row that is still 'Pending'. A render or edit
        // is claimed first and keeps the stricter transition. Either way, a refused completion means the
        // work is not recorded, so it is reported instead of being discarded silently.
        var completed = output.Operation == MediaEditOperationKind.Edit
            ? await _images.TryCompleteImageAsync(image, cancellationToken)
            : await _images.TryCompleteOperationImageAsync(image, cancellationToken);
        if (!completed)
        {
            _logger.LogWarning(
                "Scene image completion was refused and the row is unchanged: ImageId={ImageId}, Operation={Operation}, Status={Status}, FilePath={FilePath}",
                image.Id, output.Operation, image.Status, image.FileRelativePath);
            return;
        }

        await CompleteSessionIfLatestAttemptAsync(image, cancellationToken);
    }

    public async Task FailAsync(
        MediaEditRunContext context, string error, CancellationToken cancellationToken = default)
    {
        var image = await _images.GetImageAsync(context.ImageId, cancellationToken);
        if (image is null)
            return;

        image.Status = SceneImageStatus.Failed;
        image.ErrorMessage = error;
        image.UpdatedUtc = DateTime.UtcNow;
        if (!await _images.TryFailImageAsync(image, cancellationToken))
            return;

        if (!string.IsNullOrWhiteSpace(image.EditSessionId) && !string.IsNullOrWhiteSpace(image.EditCompilationAttemptId))
        {
            var latest = await _edits.GetLatestAttemptAsync(image.EditSessionId, cancellationToken);
            if (latest?.Id == image.EditCompilationAttemptId)
            {
                await _edits.UpdateSessionStatusAsync(
                    image.EditSessionId, SceneImageEditSessionStatus.Failed, DateTime.UtcNow, cancellationToken: cancellationToken);
            }
        }
    }

    private async Task CompleteSessionIfLatestAttemptAsync(SceneImageRecord image, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(image.EditSessionId) || string.IsNullOrWhiteSpace(image.EditCompilationAttemptId))
            return;

        var latest = await _edits.GetLatestAttemptAsync(image.EditSessionId, cancellationToken);
        if (latest?.Id != image.EditCompilationAttemptId)
            return;

        await _edits.UpdateSessionStatusAsync(
            image.EditSessionId, SceneImageEditSessionStatus.Completed, DateTime.UtcNow, image.CompletedUtc, cancellationToken);
    }

    /// <summary>
    /// The persisted identity binding shape written with the queued image. Declared here rather than
    /// shared from the legacy handler, which retires with the rest of the duplicate pipeline.
    /// </summary>
    private sealed record IdentityBinding(
        int Ordinal,
        string CharacterId,
        string CharacterName,
        string IdentityPackId,
        int IdentityPackVersion,
        string CanonicalFaceAssetId,
        string FileRelativePath,
        string Sha256);
}
