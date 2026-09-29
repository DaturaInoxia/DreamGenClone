using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// Trains a LoRA on the ComfyUI host over HTTP.
///
/// <para>
/// "Local" here means the machine that owns the GPU and the base checkpoints, which is NOT the machine running this
/// app: the app's own desktop has a small card and no ComfyUI. So this adapter does not spawn a trainer process. It
/// posts the job to the training service beside ComfyUI and polls it, exactly as the RunPod serverless adapter does,
/// because that is the only shape that survives the two machines being different.
/// </para>
///
/// <para>
/// The dataset travels WITH the job: the members' bytes are zipped and uploaded, so training needs no reachability
/// in the other direction (the host never has to fetch anything from the desktop). The images are found through the
/// app's own scene-asset storage, so the pixels trained on are the exact files the members' checksums name.
/// </para>
///
/// <para>
/// The service it talks to is <c>helpers/local-training/lora_train_service.py</c>, which documents the same contract
/// from the other side. Adapter key: <see cref="Key"/>.
/// </para>
/// </summary>
public sealed class LocalCharacterLoraTrainingDispatchAdapter : ICharacterLoraTrainingDispatchAdapter
{
    /// <summary>The adapter key this endpoint must name. Nothing falls back to it.</summary>
    public const string Key = "local-kohya-training-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProviderRepository _providers;
    private readonly ISceneAssetRepository _assets;
    private readonly ISceneAssetStorageService _storage;
    private readonly ILogger<LocalCharacterLoraTrainingDispatchAdapter> _logger;

    public LocalCharacterLoraTrainingDispatchAdapter(
        IHttpClientFactory httpClientFactory,
        IProviderRepository providers,
        ISceneAssetRepository assets,
        ISceneAssetStorageService storage,
        ILogger<LocalCharacterLoraTrainingDispatchAdapter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _providers = providers;
        _assets = assets;
        _storage = storage;
        _logger = logger;
    }

    public string AdapterKey => Key;

    public async Task<CharacterLoraTrainingSubmission> SubmitAsync(
        CharacterLoraTrainingRequest request, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(request.Endpoint, cancellationToken);
        var archivePath = await BuildDatasetArchiveAsync(request, cancellationToken);
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(
                new StringContent(request.CanonicalProviderRequestJson, Encoding.UTF8, "application/json"),
                "request");
            await using var archive = File.OpenRead(archivePath);
            content.Add(new StreamContent(archive), "dataset", $"dataset-{request.TrainingJobId}.zip");

            using var response = await client.PostAsync(
                BuildUri(request.Endpoint.BaseUrl, request.Endpoint.SubmitPath), content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Local LoRA training submission failed with HTTP {(int)response.StatusCode}: {body}");
            }

            using var document = JsonDocument.Parse(body);
            var runId = RequiredString(document.RootElement, "id", "Local LoRA training submission");
            _logger.LogInformation(
                "Submitted local LoRA training: Job={JobId}, Run={RunId}, Dataset={DatasetId}",
                request.TrainingJobId, runId, request.DatasetId);
            return new CharacterLoraTrainingSubmission(
                runId,
                StatusUri(request.Endpoint, runId),
                JsonSerializer.Serialize(new { id = runId, status = "IN_QUEUE" }));
        }
        finally
        {
            TryDelete(archivePath);
        }
    }

    public async Task<CharacterLoraTrainingPollResult> PollAsync(
        CharacterLoraTrainingRequest request, string providerRequestId, CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(request.Endpoint, cancellationToken);
        using var response = await client.GetAsync(
            StatusUri(request.Endpoint, providerRequestId), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Local LoRA training status failed with HTTP {(int)response.StatusCode}: {body}");
        }

        using var document = JsonDocument.Parse(body);
        var status = RequiredString(document.RootElement, "status", "Local LoRA training status");
        var state = status switch
        {
            "IN_QUEUE" => CharacterLoraTrainingProviderState.Queued,
            "IN_PROGRESS" => CharacterLoraTrainingProviderState.Running,
            "COMPLETED" => CharacterLoraTrainingProviderState.Succeeded,
            "FAILED" => CharacterLoraTrainingProviderState.Failed,
            "CANCELLED" => CharacterLoraTrainingProviderState.Cancelled,
            _ => throw new InvalidOperationException($"Unknown local LoRA training status '{status}'.")
        };

        var snapshot = JsonSerializer.Serialize(new
        {
            id = providerRequestId,
            status,
            error = OptionalString(document.RootElement, "error")
        });

        if (state != CharacterLoraTrainingProviderState.Succeeded)
        {
            var diagnostic = OptionalString(document.RootElement, "error");
            return new CharacterLoraTrainingPollResult(
                state, snapshot, JsonSerializer.Serialize(new[] { new { status } }),
                "[]", "[]", "[]", null, null, null,
                state == CharacterLoraTrainingProviderState.Failed ? "local_training_failed" : null,
                state == CharacterLoraTrainingProviderState.Failed
                    ? diagnostic ?? $"Local LoRA training ended as {status}."
                    : null);
        }

        if (!document.RootElement.TryGetProperty("output", out var output)
            || output.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Completed local LoRA training response did not contain output.");
        }

        if (!output.TryGetProperty("artifact", out var artifact) || artifact.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Completed local LoRA training response did not contain output.artifact.");
        }

        var byteLength = RequiredInt64(artifact, "byteLength", "Local LoRA artifact");
        if (byteLength <= 0)
        {
            throw new InvalidOperationException("Local LoRA artifact byteLength must be positive.");
        }

        return new CharacterLoraTrainingPollResult(
            state, snapshot,
            RequiredJson(output, "statusHistory", JsonValueKind.Array, "Local LoRA training output"),
            RequiredJson(output, "logs", JsonValueKind.Array, "Local LoRA training output"),
            RequiredJson(output, "samples", JsonValueKind.Array, "Local LoRA training output"),
            RequiredJson(output, "checkpoints", JsonValueKind.Array, "Local LoRA training output"),
            RequiredString(artifact, "fileRelativePath", "Local LoRA artifact"),
            RequiredString(artifact, "sha256", "Local LoRA artifact"),
            byteLength, null, null);
    }

    /// <summary>
    /// The members' bytes, their captions, and a manifest tying each file back to the member that produced it.
    ///
    /// The image is read by the member's OWN asset path, so a member whose file went missing fails here, before a
    /// job is submitted, rather than training a set with a hole in it.
    /// </summary>
    private async Task<string> BuildDatasetArchiveAsync(
        CharacterLoraTrainingRequest request, CancellationToken cancellationToken)
    {
        var members = JsonSerializer.Deserialize<List<CharacterLoraDatasetMember>>(
            request.DatasetMembersSnapshotJson, JsonOptions)
            ?? throw new InvalidOperationException(
                $"LoRA training job '{request.TrainingJobId}' carries no dataset members to train on.");
        if (members.Count == 0)
        {
            throw new InvalidOperationException(
                $"LoRA training job '{request.TrainingJobId}' carries no dataset members to train on.");
        }

        var archivePath = Path.Combine(Path.GetTempPath(), $"dgc-lora-{request.TrainingJobId}.zip");
        var manifest = new List<object>();

        await using (var file = File.Create(archivePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            foreach (var member in members.OrderBy(value => value.Ordinal))
            {
                var asset = await _assets.GetAsync(member.SceneAssetId, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"LoRA dataset member '{member.Id}' names an asset that no longer exists: '{member.SceneAssetId}'.");
                if (string.IsNullOrWhiteSpace(asset.FileRelativePath))
                {
                    throw new InvalidOperationException(
                        $"LoRA dataset member '{member.Id}' has no stored image file to train on.");
                }

                var extension = Path.GetExtension(asset.FileRelativePath);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = ".png";
                }

                var fileName = $"{member.Ordinal:D3}_{CellKey(member)}{extension}";
                await WriteEntryAsync(archive, $"images/{fileName}", asset.FileRelativePath, cancellationToken);
                WriteText(archive, $"images/{Path.GetFileNameWithoutExtension(fileName)}.txt", member.Caption);

                manifest.Add(new
                {
                    ordinal = member.Ordinal,
                    cellKey = CellKey(member),
                    split = member.Split.ToString(),
                    role = member.Role.ToString(),
                    fileName,
                    assetSha256 = member.AssetSha256
                });
            }

            WriteText(archive, "images/manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));
        }

        return archivePath;
    }

    /// <summary>The cell a member came from, read from its own coverage snapshot rather than assumed from its id.</summary>
    private static string CellKey(CharacterLoraDatasetMember member)
    {
        try
        {
            using var coverage = JsonDocument.Parse(member.CoverageJson);
            if (coverage.RootElement.TryGetProperty("cellKey", out var cellKey)
                && cellKey.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(cellKey.GetString()))
            {
                return cellKey.GetString()!;
            }
        }
        catch (JsonException)
        {
            // A member whose coverage snapshot is unreadable is named by its id instead of silently sharing a name.
        }

        return member.Id;
    }

    private async Task WriteEntryAsync(
        ZipArchive archive, string entryName, string relativePath, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var target = entry.Open();
        await using var source = await _storage.OpenReadAsync(relativePath, cancellationToken);
        await source.CopyToAsync(target, cancellationToken);
    }

    private static void WriteText(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private async Task<HttpClient> CreateClientAsync(
        CharacterLoraTrainingEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (!string.Equals(endpoint.AdapterKey, Key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Local LoRA training endpoint adapter must be '{Key}'.");
        }

        var provider = await _providers.GetByIdAsync(endpoint.EndpointId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Configured LoRA training provider endpoint '{endpoint.EndpointId}' was not found.");
        if (!provider.IsEnabled
            || !string.Equals(provider.Name, endpoint.ProviderKey, StringComparison.Ordinal)
            || !string.Equals(provider.BaseUrl.TrimEnd('/'), endpoint.BaseUrl.TrimEnd('/'), StringComparison.Ordinal)
            || provider.TimeoutSeconds != endpoint.TimeoutSeconds)
        {
            throw new InvalidOperationException(
                $"LoRA training endpoint '{endpoint.EndpointId}' no longer matches provider '{provider.Name}'. "
                + "The endpoint is a snapshot taken when the job was prepared; re-prepare the job.");
        }

        var client = _httpClientFactory.CreateClient(nameof(LocalCharacterLoraTrainingDispatchAdapter));
        client.Timeout = TimeSpan.FromSeconds(endpoint.TimeoutSeconds);
        return client;
    }

    private static string StatusUri(CharacterLoraTrainingEndpoint endpoint, string providerRequestId) =>
        BuildUri(
            endpoint.BaseUrl,
            endpoint.StatusPathTemplate.Replace("{jobId}", Uri.EscapeDataString(providerRequestId), StringComparison.Ordinal));

    private static string BuildUri(string baseUrl, string path) => $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static string RequiredString(JsonElement element, string property, string label) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException($"{label} did not contain '{property}'.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long RequiredInt64(JsonElement element, string property, string label) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : throw new InvalidOperationException($"{label} did not contain a numeric '{property}'.");

    private static string RequiredJson(JsonElement element, string property, JsonValueKind kind, string label) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == kind
            ? value.GetRawText()
            : throw new InvalidOperationException($"{label} did not contain a '{property}' {kind}.");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A temporary upload that cannot be deleted is not a training failure.
        }
    }
}
