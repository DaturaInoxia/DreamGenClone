using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The local training path.
///
/// <para>
/// "Local" means the machine that owns the GPU and the checkpoints, which is not the machine this app runs on, so
/// the adapter's job is to carry the dataset TO the trainer and read its verdict back. Those two things are pinned
/// here: the upload must contain every member's exact bytes and caption with a manifest that names them, and the
/// poll must map the trainer's status and artifact without inventing either.
/// </para>
/// </summary>
public sealed class LocalCharacterLoraTrainingDispatchAdapterTests
{
    private const string Sha256 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task Submit_UploadsEveryMemberWithItsCaptionAndAManifestNamingThem()
    {
        var handler = new RecordingHandler("""{"id":"run-1"}""");
        var adapter = Fixture.Adapter(handler);

        var submission = await Fixture.Adapter(handler).SubmitAsync(Fixture.Request());

        Assert.Equal("run-1", submission.ProviderRequestId);
        var upload = handler.Requests.Single();
        Assert.Equal("/train", upload.Path);

        var parts = ReadMultipart(upload.Body, upload.ContentType);
        Assert.Contains("request", parts.Keys);
        Assert.Contains("dataset", parts.Keys);
        using var part = new ZipArchive(new MemoryStream(parts["dataset"]), ZipArchiveMode.Read);
        var names = part.Entries.Select(entry => entry.FullName).ToList();
        Assert.Contains("images/000_core.front.cu.1.png", names);
        Assert.Contains("images/000_core.front.cu.1.txt", names);
        Assert.Contains("images/001_var.front.cu.1.png", names);
        Assert.Contains("images/manifest.json", names);

        var manifest = JsonDocument.Parse(ReadEntry(part, "images/manifest.json"));
        Assert.Equal(2, manifest.RootElement.GetArrayLength());
        Assert.Equal("core.front.cu.1", manifest.RootElement[0].GetProperty("cellKey").GetString());
        Assert.Equal("Train", manifest.RootElement[0].GetProperty("split").GetString());
        Assert.Equal("Validation", manifest.RootElement[1].GetProperty("split").GetString());

        Assert.Equal("the caption for cell one", ReadEntry(part, "images/000_core.front.cu.1.txt"));
    }

    [Fact]
    public async Task Poll_MapsACompletedRunToTheArtifactTheTrainerPublished()
    {
        var handler = new RecordingHandler("""
            {"status":"COMPLETED","error":null,"output":{
              "artifact":{"fileRelativePath":"D:\\ComfyUI\\models\\loras\\dgc_lora_x.safetensors",
                          "sha256":"BBBB","byteLength":2048,"loraName":"dgc_lora_x.safetensors"},
              "statusHistory":[{"status":"IN_QUEUE"}],"logs":[],"samples":[],"checkpoints":[]}}
            """);

        var result = await Fixture.Adapter(handler).PollAsync(Fixture.Request(), "run-1");

        Assert.Equal(CharacterLoraTrainingProviderState.Succeeded, result.State);
        Assert.Equal("D:\\ComfyUI\\models\\loras\\dgc_lora_x.safetensors", result.OutputFileRelativePath);
        Assert.Equal("BBBB", result.OutputSha256);
        Assert.Equal(2048, result.OutputByteLength);
        Assert.Contains("IN_QUEUE", result.StatusHistoryJson, StringComparison.Ordinal);
        Assert.Equal("/train/run-1", handler.Requests.Single().Path);
    }

    [Fact]
    public async Task Poll_CarriesTheTrainersOwnDiagnosticOnFailure()
    {
        var handler = new RecordingHandler(
            """{"status":"FAILED","error":"CUDA out of memory while loading the base model."}""");

        var result = await Fixture.Adapter(handler).PollAsync(Fixture.Request(), "run-1");

        Assert.Equal(CharacterLoraTrainingProviderState.Failed, result.State);
        Assert.Equal("local_training_failed", result.FailureCode);
        Assert.Equal("CUDA out of memory while loading the base model.", result.FailureDiagnostic);
    }

    [Fact]
    public async Task Submit_RefusesAMemberWhoseImageIsGoneBeforeItSubmitsAnything()
    {
        var handler = new RecordingHandler("""{"id":"run-1"}""");
        var adapter = Fixture.Adapter(handler, assetWithoutFile: true);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.SubmitAsync(Fixture.Request()));

        Assert.Contains("no stored image file", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Splits a multipart body well enough to get at the zip, without pulling in a multipart parser.</summary>
    private static Dictionary<string, byte[]> ReadMultipart(byte[] body, string contentType)
    {
        var boundary = contentType.Split("boundary=")[1].Trim('"');
        var text = Encoding.Latin1.GetString(body);
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var segments = text.Split($"--{boundary}");

        foreach (var segment in segments)
        {
            // .NET writes form-data names unquoted (name=request), so the name is read to the next delimiter and
            // any quoting is stripped rather than assumed.
            var nameIndex = segment.IndexOf("name=", StringComparison.Ordinal);
            if (nameIndex < 0)
            {
                continue;
            }

            var rest = segment[(nameIndex + 5)..];
            var terminator = rest.IndexOfAny(['\r', ';', '\n']);
            var name = (terminator < 0 ? rest : rest[..terminator]).Trim('"');
            var headerEnd = segment.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var payload = segment[(headerEnd + 4)..].TrimEnd('\r', '\n', '-');
            parts[name] = Encoding.Latin1.GetBytes(payload);
        }

        return parts;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;

        public RecordingHandler(string response) => _response = response;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.RequestUri!.AbsolutePath,
                request.Content?.Headers.ContentType?.ToString() ?? string.Empty,
                body));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record RecordedRequest(string Path, string ContentType, byte[] Body);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class StubProviderRepository : IProviderRepository
    {
        public Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Provider?>(new Provider
            {
                Id = Fixture.EndpointId,
                Name = Fixture.ProviderKey,
                BaseUrl = Fixture.BaseUrl,
                IsEnabled = true,
                TimeoutSeconds = 60
            });

        public Task<Provider> SaveAsync(Provider provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Provider>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubSceneAssetRepository : ISceneAssetRepository
    {
        private readonly bool _withoutFile;

        public StubSceneAssetRepository(bool withoutFile) => _withoutFile = withoutFile;

        public Task<SceneAsset?> GetAsync(string assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SceneAsset?>(new SceneAsset
            {
                Id = assetId,
                Name = assetId,
                Kind = SceneAssetKind.PromotedApprovedFrame,
                Status = SceneAssetStatus.Complete,
                Type = SceneAssetType.ProductionFrame,
                FileRelativePath = _withoutFile ? null : $"assets/{assetId}.png",
                MediaType = "image/png",
                ByteLength = 4,
                Sha256 = Sha256,
                CompletedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });

        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAsset>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAsset>> ListByPackAsync(string identityPackId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAsset>> ListByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertImageAsync(SceneAssetImage image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImagePromptAsync(string imageId, string prompt, string promptCompilerId, string? negativePrompt, string? associationMetadataJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImageCandidateDecisionAsync(string imageId, SceneAssetCandidateDecision decision, string? notes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAssetImage> ApproveImageForProductionAsync(string imageId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertAsync(SceneAsset asset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateCandidateFieldsAsync(string assetId, string? candidateBatchId, SceneAssetCandidateDecision? candidateDecision, string? candidateNotes, string? candidateSourceAssetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> ApproveForProductionAsync(string assetId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreatePromotedAsync(SceneAsset asset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubStorage : ISceneAssetStorageService
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("PNGDATA")));

        public Task<StoredSceneAsset> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class QuietLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private static class Fixture
    {
        public const string ProviderKey = "Local LoRA trainer";
        public const string BaseUrl = "http://wood-game-main:8199";
        public const string EndpointId = "provider-local-trainer";

        public static LocalCharacterLoraTrainingDispatchAdapter Adapter(
            HttpMessageHandler handler, bool assetWithoutFile = false) =>
            new(
                new StubHttpClientFactory(handler),
                new StubProviderRepository(),
                new StubSceneAssetRepository(assetWithoutFile),
                new StubStorage(),
                new QuietLogger<LocalCharacterLoraTrainingDispatchAdapter>());

        public static CharacterLoraTrainingRequest Request() => new(
            "job-1",
            "dataset-1",
            Sha256,
            "{\"name\":\"sdxl character\",\"version\":1}",
            JsonSerializer.Serialize(new[]
            {
                Member(0, "core.front.cu.1", CharacterLoraDatasetSplit.Train, "asset-1", "the caption for cell one"),
                Member(1, "var.front.cu.1", CharacterLoraDatasetSplit.Validation, "asset-2", "the caption for cell two")
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            "{\"id\":\"job-1\",\"datasetId\":\"dataset-1\",\"baseModelId\":\"juggernautXL_ragnarok.safetensors\"}",
            new CharacterLoraTrainingEndpoint(
                LocalCharacterLoraTrainingDispatchAdapter.Key, ProviderKey, EndpointId, BaseUrl,
                "/train", "/train/{jobId}", "/train/{jobId}/cancel", 60),
            12345,
            1);

        private static CharacterLoraDatasetMember Member(
            int ordinal, string cellKey, CharacterLoraDatasetSplit split, string assetId, string caption) => new()
            {
                Id = $"member-{ordinal}",
                DatasetId = "dataset-1",
                Ordinal = ordinal,
                SceneAssetId = assetId,
                SceneAssetVersion = 1,
                AssetSha256 = Sha256,
                Role = split == CharacterLoraDatasetSplit.Train
                    ? CharacterLoraDatasetMemberRole.Training
                    : CharacterLoraDatasetMemberRole.Validation,
                Split = split,
                Caption = caption,
                CaptionRevision = 1,
                CoverageJson = $"{{\"cellKey\":\"{cellKey}\"}}",
                GenerationAttemptId = $"attempt-{ordinal}",
                CurationStatus = CharacterLoraCurationStatus.Accepted,
                CurationFindingsJson = "{}",
                ReviewedBy = "curator-1",
                ReviewedUtc = DateTime.UtcNow
            };
    }
}
