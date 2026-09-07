using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneAssetImageEditCompilationJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISceneAssetImageEditRepository _editRepository;
    private readonly ISceneAssetRepository _assetRepository;
    private readonly ISceneAssetStorageService _storage;
    private readonly IMultimodalModelResolutionService _modelResolver;
    private readonly IMultimodalCompletionClient _completionClient;
    private readonly ISceneImageEditPromptCompiler _compiler;

    public SceneAssetImageEditCompilationJobHandler(ISceneAssetImageEditRepository editRepository, ISceneAssetRepository assetRepository, ISceneAssetStorageService storage, IMultimodalModelResolutionService modelResolver, IMultimodalCompletionClient completionClient, ISceneImageEditPromptCompiler compiler)
    {
        _editRepository = editRepository;
        _assetRepository = assetRepository;
        _storage = storage;
        _modelResolver = modelResolver;
        _completionClient = completionClient;
        _compiler = compiler;
    }

    public string JobType => BackgroundJobTypes.SceneAssetImageEditPromptCompilation;

    public Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        => HandleAsync(job.PayloadJson, cancellationToken);

    private async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneAssetImageEditCompilationJobPayload>(payloadJson, JsonOptions) ?? throw new InvalidOperationException("Asset edit compilation payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.AttemptId)) throw new InvalidOperationException("Asset edit compilation payload requires an attempt id.");
        var attempt = await _editRepository.GetAttemptAsync(payload.AttemptId, cancellationToken) ?? throw new InvalidOperationException($"Asset compilation attempt '{payload.AttemptId}' was not found.");
        if (attempt.Status is SceneImageEditCompilationAttemptStatus.Ready or SceneImageEditCompilationAttemptStatus.ClarificationRequired or SceneImageEditCompilationAttemptStatus.Invalid or SceneImageEditCompilationAttemptStatus.Failed) return;
        if (attempt.Status != SceneImageEditCompilationAttemptStatus.Pending) throw new InvalidOperationException($"Asset compilation attempt '{attempt.Id}' is already processing.");
        attempt.Status = SceneImageEditCompilationAttemptStatus.Compiling;
        attempt.StartedUtc = DateTime.UtcNow;
        await _editRepository.UpdateAttemptAsync(attempt, cancellationToken);
        SceneAssetImageEditSession? session = null;
        try
        {
            session = await _editRepository.GetSessionAsync(attempt.EditSessionId, cancellationToken) ?? throw new InvalidOperationException($"Asset edit session '{attempt.EditSessionId}' was not found.");
            var source = await RequireSourceAsync(session, cancellationToken);
            var resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);
            if (!string.Equals(SceneImageMultimodalInput.SerializeResolutionSnapshot(resolved), attempt.ResolvedModelSnapshotJson, StringComparison.Ordinal)) throw new InvalidOperationException("The compiler model configuration changed after this asset attempt was queued.");
            await using var stream = await _storage.OpenReadAsync(source.FileRelativePath!, cancellationToken);
            var input = await SceneImageMultimodalInput.ReadAsync(stream, resolved.MaximumInputImageBytes, cancellationToken);
            SceneImageMultimodalInput.Validate(input, resolved);
            RequireChecksum(input.Sha256, session, attempt);
            var history = string.IsNullOrWhiteSpace(attempt.ClarificationContextJson) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(attempt.ClarificationContextJson, JsonOptions) ?? throw new InvalidOperationException("The asset compilation clarification snapshot is invalid.");
            var messages = _compiler.BuildMessages(new SceneImageEditCompilerContext(attempt.RawIntent, history));
            if (messages.SchemaVersion != attempt.CompilerSchemaVersion || messages.SystemPromptVersion != attempt.SystemPromptVersion) throw new InvalidOperationException("The compiler prompt contract changed after this asset attempt was queued.");
            await _completionClient.CheckHealthAsync(resolved, cancellationToken);
            var completion = await _completionClient.GenerateAsync(resolved, new MultimodalCompletionRequest(messages.SystemMessage, messages.UserMessage, new MultimodalImageInput(input.MediaType, input.Bytes, input.Width, input.Height, input.Sha256), messages.ResponseSchemaName, messages.ResponseSchema), cancellationToken);
            attempt.RawModelResponse = completion.Content;
            var result = _compiler.Parse(completion.Content, input.Width, input.Height);
            attempt.ParsedResultJson = JsonSerializer.Serialize(result, JsonOptions);
            attempt.Status = result.Status switch { SceneImageEditCompilationResultStatus.Ready => SceneImageEditCompilationAttemptStatus.Ready, SceneImageEditCompilationResultStatus.ClarificationRequired => SceneImageEditCompilationAttemptStatus.ClarificationRequired, SceneImageEditCompilationResultStatus.Invalid => SceneImageEditCompilationAttemptStatus.Invalid, _ => throw new InvalidOperationException("The compiler returned a non-terminal result.") };
            attempt.CompletedUtc = DateTime.UtcNow;
            await _editRepository.UpdateAttemptAsync(attempt, cancellationToken);
            if (result.Status == SceneImageEditCompilationResultStatus.Ready)
            {
                var prompt = result.CompiledPrompt!;
                await _editRepository.CreateRevisionAsync(new SceneAssetImageEditPromptRevision { CompilationAttemptId = attempt.Id, Ordinal = 0, Prompt = prompt, RevisionKind = SceneImageEditPromptRevisionKind.CompilerOutput, PromptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))) }, cancellationToken);
            }
            await _editRepository.UpdateSessionStatusAsync(session.Id, result.Status switch { SceneImageEditCompilationResultStatus.Ready => SceneAssetImageEditSessionStatus.Ready, SceneImageEditCompilationResultStatus.ClarificationRequired => SceneAssetImageEditSessionStatus.ClarificationRequired, SceneImageEditCompilationResultStatus.Invalid => SceneAssetImageEditSessionStatus.Invalid, _ => throw new InvalidOperationException("The compiler returned a non-terminal result.") }, DateTime.UtcNow, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            attempt.Status = SceneImageEditCompilationAttemptStatus.Failed;
            attempt.Error = ex.Message;
            attempt.CompletedUtc = DateTime.UtcNow;
            await _editRepository.UpdateAttemptAsync(attempt, cancellationToken);
            if (session is not null) await _editRepository.UpdateSessionStatusAsync(session.Id, SceneAssetImageEditSessionStatus.Failed, DateTime.UtcNow, cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task<SceneAssetImage> RequireSourceAsync(SceneAssetImageEditSession session, CancellationToken cancellationToken)
    {
        var source = await _assetRepository.GetImageAsync(session.SourceImageId, cancellationToken) ?? throw new InvalidOperationException($"Source scene asset image '{session.SourceImageId}' was not found.");
        if (!string.Equals(source.AssetId, session.AssetId, StringComparison.Ordinal) || source.Status != SceneAssetStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath)) throw new InvalidOperationException("Asset edit compilation requires the stored complete source image owned by its session asset.");
        return source;
    }

    private static void RequireChecksum(string actual, SceneAssetImageEditSession session, SceneAssetImageEditCompilationAttempt attempt)
    {
        if (!string.Equals(actual, session.SourceImageSha256, StringComparison.OrdinalIgnoreCase) || !string.Equals(actual, attempt.SourceImageSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The source asset image checksum changed after compilation was queued.");
    }
}