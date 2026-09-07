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

public sealed class SceneAssetImageEditDescriptionJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISceneAssetImageEditRepository _editRepository;
    private readonly ISceneAssetRepository _assetRepository;
    private readonly ISceneAssetStorageService _storage;
    private readonly IMultimodalModelResolutionService _modelResolver;
    private readonly IMultimodalCompletionClient _completionClient;

    public SceneAssetImageEditDescriptionJobHandler(ISceneAssetImageEditRepository editRepository, ISceneAssetRepository assetRepository, ISceneAssetStorageService storage, IMultimodalModelResolutionService modelResolver, IMultimodalCompletionClient completionClient)
    {
        _editRepository = editRepository;
        _assetRepository = assetRepository;
        _storage = storage;
        _modelResolver = modelResolver;
        _completionClient = completionClient;
    }

    public string JobType => BackgroundJobTypes.SceneAssetImageEditDescription;

    public Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        => HandleAsync(job.PayloadJson, cancellationToken);

    private async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneAssetImageEditDescriptionJobPayload>(payloadJson, JsonOptions) ?? throw new InvalidOperationException("Asset edit description payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.EditSessionId)) throw new InvalidOperationException("Asset edit description payload requires an edit session id.");
        var session = await _editRepository.GetSessionAsync(payload.EditSessionId, cancellationToken) ?? throw new InvalidOperationException($"Asset edit session '{payload.EditSessionId}' was not found.");
        if (!string.IsNullOrWhiteSpace(session.DescriptionText)) return;
        var source = await _assetRepository.GetImageAsync(session.SourceImageId, cancellationToken) ?? throw new InvalidOperationException($"Source scene asset image '{session.SourceImageId}' was not found.");
        if (!string.Equals(source.AssetId, session.AssetId, StringComparison.Ordinal) || source.Status != SceneAssetStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath)) throw new InvalidOperationException("Asset edit description requires the stored complete source image owned by its session asset.");
        var resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);
        await using var stream = await _storage.OpenReadAsync(source.FileRelativePath, cancellationToken);
        var input = await SceneImageMultimodalInput.ReadAsync(stream, resolved.MaximumInputImageBytes, cancellationToken);
        SceneImageMultimodalInput.Validate(input, resolved);
        if (!string.Equals(input.Sha256, session.SourceImageSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The source asset image checksum changed after the edit session was created.");
        await _completionClient.CheckHealthAsync(resolved, cancellationToken);
        var completion = await _completionClient.GenerateAsync(resolved, new MultimodalCompletionRequest(SceneImageDescriptionPromptBuilder.BuildSystemMessage(), SceneImageDescriptionPromptBuilder.BuildUserMessage(), new MultimodalImageInput(input.MediaType, input.Bytes, input.Width, input.Height, input.Sha256), SceneImageDescriptionPromptBuilder.ResponseSchemaName, SceneImageDescriptionPromptBuilder.CreateResponseSchema()), cancellationToken);
        await _editRepository.SetDescriptionAsync(session.Id, ParseDescription(completion.Content), DateTime.UtcNow, cancellationToken);
    }

    private static string ParseDescription(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) throw new InvalidOperationException("Asset image edit description returned empty output.");
        try
        {
            using var document = JsonDocument.Parse(rawResponse);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("description", out var description) || description.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(description.GetString())) throw new InvalidOperationException("Asset image edit description response must contain a non-empty 'description' string.");
            return description.GetString()!.Trim();
        }
        catch (JsonException ex) { throw new InvalidOperationException("Asset image edit description returned malformed JSON.", ex); }
    }
}