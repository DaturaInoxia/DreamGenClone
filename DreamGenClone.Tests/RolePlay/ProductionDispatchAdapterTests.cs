using System.Net;
using System.Text;
using System.Text.Json;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Models;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ProductionDispatchAdapterTests
{
    [Fact]
    public async Task RunPod_SubmitsWorkerComfyUiEnvelopeAndReturnsImmediateJobId()
    {
        var requests = new List<(HttpMethod Method, string Uri, string Body, string? Authorization)>();
        var handler = new QueueHandler(requests,
            Json(HttpStatusCode.OK, "{\"id\":\"runpod-job-1\"}"),
            Json(HttpStatusCode.OK, "{\"id\":\"runpod-job-1\",\"status\":\"COMPLETED\",\"output\":{\"images\":[{\"type\":\"base64\",\"data\":\"AQID\"}]}}"));
        var provider = Provider("runpod-endpoint", ImageProtocol.ComfyUiServerless, ProviderType.TogetherAI);
        var adapter = new RunPodProductionDispatchAdapter(
            new SingleClientFactory(handler), new ProviderStub(provider), new EncryptionStub());
        var endpoint = Endpoint(provider, RunPodProductionDispatchAdapter.Key);
        var group = Group(endpoint, RunPodProductionDispatchAdapter.Key, nativeVariations: false, attemptCount: 1);

        var submission = Assert.Single(await adapter.SubmitAsync(group));
        var result = await adapter.PollAsync(endpoint, submission.ProviderRequestId);

        Assert.Equal("runpod-job-1", submission.ProviderRequestId);
        Assert.Equal("https://provider.invalid/v2/endpoint/run", requests[0].Uri);
        Assert.Equal("Bearer plain-key", requests[0].Authorization);
        using var body = JsonDocument.Parse(requests[0].Body);
        Assert.Equal(1024, body.RootElement.GetProperty("input").GetProperty("workflow").GetProperty("width").GetInt32());
        Assert.Equal("https://provider.invalid/v2/endpoint/status/runpod-job-1", requests[1].Uri);
        Assert.Equal(ProductionProviderJobState.Succeeded, result.State);
        Assert.Equal("AQID", Assert.Single(result.Outputs).Base64Data);
    }

    [Fact]
    public async Task Together_UsesNativeVariationCountAndMapsEveryOutputToAnAttempt()
    {
        var requests = new List<(HttpMethod Method, string Uri, string Body, string? Authorization)>();
        var handler = new QueueHandler(requests, Json(HttpStatusCode.OK,
            "{\"id\":\"together-response-1\",\"data\":[{\"b64_json\":\"AQ==\"},{\"url\":\"https://result.invalid/2.png\"}]}"));
        var provider = Provider("together-endpoint", ImageProtocol.OpenAiImages, ProviderType.TogetherAI);
        var adapter = new TogetherProductionDispatchAdapter(
            new SingleClientFactory(handler), new ProviderStub(provider), new EncryptionStub());
        var endpoint = Endpoint(provider, TogetherProductionDispatchAdapter.Key);
        var group = Group(endpoint, TogetherProductionDispatchAdapter.Key, nativeVariations: true, attemptCount: 2);

        var submissions = await adapter.SubmitAsync(group);

        Assert.Equal(2, submissions.Count);
        Assert.Equal("together-response-1:variation:0", submissions[0].ProviderRequestId);
        Assert.Equal("together-response-1:variation:1", submissions[1].ProviderRequestId);
        Assert.Equal("https://provider.invalid/v2/endpoint/run", requests[0].Uri);
        using var body = JsonDocument.Parse(requests[0].Body);
        Assert.Equal(2, body.RootElement.GetProperty("n").GetInt32());
        Assert.Equal("AQ==", Assert.Single(submissions[0].Outputs).Base64Data);
        Assert.Equal("https://result.invalid/2.png", Assert.Single(submissions[1].Outputs).TransientUrl);
    }

    [Fact]
    public async Task RunPodLoraTraining_SubmitsExactSnapshotAndRequiresCompleteOwnedResult()
    {
        var requests = new List<(HttpMethod Method, string Uri, string Body, string? Authorization)>();
        var handler = new QueueHandler(requests,
            Json(HttpStatusCode.OK, "{\"id\":\"training-job-1\"}"),
            Json(HttpStatusCode.OK, """
                {"id":"training-job-1","status":"COMPLETED","output":{
                  "statusHistory":[{"status":"COMPLETED"}],
                  "logs":[{"path":"train.log"}],
                  "samples":[{"path":"sample-1.png"}],
                  "checkpoints":[{"path":"checkpoint-1000"}],
                  "artifact":{"fileRelativePath":"lora/output.safetensors","sha256":"CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC","byteLength":1024}}}
                """));
        var provider = Provider("training-endpoint", ImageProtocol.ComfyUiServerless, ProviderType.TogetherAI);
        var adapter = new RunPodCharacterLoraTrainingDispatchAdapter(
            new SingleClientFactory(handler), new ProviderStub(provider), new EncryptionStub());
        var endpoint = new CharacterLoraTrainingEndpoint(
            RunPodCharacterLoraTrainingDispatchAdapter.Key, provider.Name, provider.Id, provider.BaseUrl,
            "/run", "/status/{jobId}", "/cancel/{jobId}", provider.TimeoutSeconds);
        var request = new CharacterLoraTrainingRequest(
            "job-1", "dataset-1", new string('A', 64), "{}", "[]",
            "{\"trainer\":\"ai-toolkit\",\"seed\":42}", endpoint, 42, 1);

        var submission = await adapter.SubmitAsync(request);
        var result = await adapter.PollAsync(request, submission.ProviderRequestId);

        Assert.Equal("training-job-1", submission.ProviderRequestId);
        Assert.Equal("https://provider.invalid/v2/endpoint/run", requests[0].Uri);
        Assert.Equal("Bearer plain-key", requests[0].Authorization);
        using var body = JsonDocument.Parse(requests[0].Body);
        Assert.Equal("ai-toolkit", body.RootElement.GetProperty("input").GetProperty("trainer").GetString());
        Assert.Equal("https://provider.invalid/v2/endpoint/status/training-job-1", requests[1].Uri);
        Assert.Equal(CharacterLoraTrainingProviderState.Succeeded, result.State);
        Assert.Equal("lora/output.safetensors", result.OutputFileRelativePath);
        Assert.Contains("sample-1.png", result.SampleManifestJson, StringComparison.Ordinal);
        Assert.Contains("checkpoint-1000", result.CheckpointManifestJson, StringComparison.Ordinal);
    }

    private static ProductionProviderEndpoint Endpoint(Provider provider, string protocol) => new(
        "provider-key", provider.Id, provider.BaseUrl, "/run", "/status/{jobId}", "/cancel/{jobId}",
        provider.TimeoutSeconds, protocol, "{\"ready\":true}");

    private static ProductionDispatchGroup Group(
        ProductionProviderEndpoint endpoint,
        string adapterKey,
        bool nativeVariations,
        int attemptCount)
    {
        var request = new CompiledMediaRequest
        {
            Id = "request-1", ProviderKey = "provider-key", ModelId = "model-1", ModelVersion = "v1",
            WorkflowRevision = "workflow-1", CompilerId = "compiler-1", CompilerVersion = "1",
            RequestSchemaVersion = "request-v1",
            CanonicalProviderRequestJson = "{\"model\":\"model-1\",\"width\":1024,\"height\":1024,\"seed\":42}"
        };
        var attempts = Enumerable.Range(1, attemptCount).Select(index => new ProductionDispatchAttempt(
            new ProductionAttempt { Id = $"attempt-{index}" }, request)).ToList();
        return new ProductionDispatchGroup(
            "group-1", endpoint,
            new ProductionDispatchPolicy(adapterKey, nativeVariations, 4, "worker:v1", "artifacts:v1", "inline", 600),
            attempts);
    }

    private static Provider Provider(string id, ImageProtocol protocol, ProviderType type) => new()
    {
        Id = id, Name = id, ProviderType = type, BaseUrl = "https://provider.invalid/v2/endpoint",
        ImageProtocol = protocol, TimeoutSeconds = 30, ApiKeyEncrypted = "encrypted-key", IsEnabled = true
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string Uri, string Body, string? Authorization)> _requests;
        private readonly Queue<HttpResponseMessage> _responses;

        public QueueHandler(
            List<(HttpMethod Method, string Uri, string Body, string? Authorization)> requests,
            params HttpResponseMessage[] responses)
        {
            _requests = requests;
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Add((request.Method, request.RequestUri!.ToString(),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString()));
            return _responses.Dequeue();
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class EncryptionStub : IApiKeyEncryptionService
    {
        public string Encrypt(string plainTextApiKey) => throw new NotSupportedException();
        public string Decrypt(string encryptedApiKey) => "plain-key";
    }

    private sealed class ProviderStub(Provider provider) : IProviderRepository
    {
        public Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Provider?>(string.Equals(id, provider.Id, StringComparison.Ordinal) ? provider : null);

        public Task<Provider> SaveAsync(Provider value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Provider>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}