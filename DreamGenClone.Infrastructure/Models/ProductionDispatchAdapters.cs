using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Infrastructure.Models;

public sealed class RunPodProductionDispatchAdapter : IProductionDispatchAdapter
{
    public const string Key = "runpod-serverless-worker-comfyui-v1";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProviderRepository _providers;
    private readonly IApiKeyEncryptionService _encryption;

    public RunPodProductionDispatchAdapter(
        IHttpClientFactory httpClientFactory,
        IProviderRepository providers,
        IApiKeyEncryptionService encryption)
    {
        _httpClientFactory = httpClientFactory;
        _providers = providers;
        _encryption = encryption;
    }

    public string AdapterKey => Key;

    public async Task<IReadOnlyList<ProductionProviderSubmission>> SubmitAsync(
        ProductionDispatchGroup group,
        CancellationToken cancellationToken = default)
    {
        ValidatePolicy(group);
        var client = await CreateClientAsync(group.Endpoint, ImageProtocol.ComfyUiServerless, cancellationToken);
        var results = new List<ProductionProviderSubmission>();
        foreach (var dispatch in group.Attempts)
        {
            var workflow = JsonNode.Parse(dispatch.Request.CanonicalProviderRequestJson)
                ?? throw new InvalidOperationException("Compiled RunPod workflow JSON was null.");
            var payload = new JsonObject
            {
                ["input"] = new JsonObject { ["workflow"] = workflow }
            };
            using var response = await client.PostAsJsonAsync(
                BuildUri(group.Endpoint.BaseUrl, group.Endpoint.SubmitPath), payload, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"RunPod submission failed with HTTP {(int)response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            var providerRequestId = RequiredString(document.RootElement, "id", "RunPod submission");
            results.Add(new ProductionProviderSubmission(
                dispatch.Attempt.Id,
                providerRequestId,
                StatusUri(group.Endpoint, providerRequestId),
                ProductionProviderJobState.Queued,
                JsonSerializer.Serialize(new { id = providerRequestId, status = "IN_QUEUE" }),
                "{\"providerReported\":false}",
                []));
        }
        return results;
    }

    public async Task<ProductionProviderPollResult> PollAsync(
        ProductionProviderEndpoint endpoint,
        string providerRequestId,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(endpoint, ImageProtocol.ComfyUiServerless, cancellationToken);
        using var response = await client.GetAsync(StatusUri(endpoint, providerRequestId), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"RunPod status failed with HTTP {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var status = RequiredString(document.RootElement, "status", "RunPod status");
        var state = status switch
        {
            "IN_QUEUE" => ProductionProviderJobState.Queued,
            "IN_PROGRESS" => ProductionProviderJobState.Running,
            "COMPLETED" => ProductionProviderJobState.Succeeded,
            "FAILED" or "TIMED_OUT" => ProductionProviderJobState.Failed,
            "CANCELLED" => ProductionProviderJobState.Cancelled,
            _ => throw new InvalidOperationException($"Unknown RunPod job status '{status}'.")
        };
        var outputs = state == ProductionProviderJobState.Succeeded
            ? ParseRunPodOutputs(document.RootElement)
            : [];
        var error = document.RootElement.TryGetProperty("error", out var errorElement)
            ? errorElement.ToString()
            : null;
        var snapshot = JsonSerializer.Serialize(new
        {
            id = providerRequestId,
            status,
            error,
            executionTime = OptionalNumber(document.RootElement, "executionTime"),
            delayTime = OptionalNumber(document.RootElement, "delayTime")
        });
        return new ProductionProviderPollResult(
            state, snapshot, "{\"providerReported\":false}", outputs,
            state == ProductionProviderJobState.Failed ? "runpod_job_failed" : null,
            state == ProductionProviderJobState.Failed ? error ?? $"RunPod job ended as {status}." : null);
    }

    public async Task CancelAsync(
        ProductionProviderEndpoint endpoint,
        string providerRequestId,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(endpoint, ImageProtocol.ComfyUiServerless, cancellationToken);
        var path = endpoint.CancelPathTemplate.Replace("{jobId}", Uri.EscapeDataString(providerRequestId), StringComparison.Ordinal);
        using var response = await client.PostAsync(BuildUri(endpoint.BaseUrl, path), null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"RunPod cancellation failed with HTTP {(int)response.StatusCode}: {body}");
        }
    }

    private async Task<HttpClient> CreateClientAsync(
        ProductionProviderEndpoint endpoint,
        ImageProtocol requiredProtocol,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(endpoint.ProtocolKey, Key, StringComparison.Ordinal))
            throw new InvalidOperationException($"RunPod endpoint protocol must be '{Key}'.");
        var provider = await RequireProviderAsync(endpoint, requiredProtocol, cancellationToken);
        var apiKey = _encryption.Decrypt(provider.ApiKeyEncrypted!);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"Provider '{provider.Id}' API key decrypted to an empty value.");
        var client = _httpClientFactory.CreateClient("CompletionClient");
        client.Timeout = TimeSpan.FromSeconds(endpoint.TimeoutSeconds);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private async Task<Provider> RequireProviderAsync(
        ProductionProviderEndpoint endpoint,
        ImageProtocol requiredProtocol,
        CancellationToken cancellationToken)
    {
        var provider = await _providers.GetByIdAsync(endpoint.EndpointId, cancellationToken)
            ?? throw new InvalidOperationException($"Configured provider endpoint '{endpoint.EndpointId}' was not found.");
        if (!provider.IsEnabled || provider.ImageProtocol != requiredProtocol
            || !string.Equals(provider.BaseUrl.TrimEnd('/'), endpoint.BaseUrl.TrimEnd('/'), StringComparison.Ordinal)
            || provider.TimeoutSeconds != endpoint.TimeoutSeconds
            || string.IsNullOrWhiteSpace(provider.ApiKeyEncrypted))
        {
            throw new InvalidOperationException($"Provider endpoint '{endpoint.EndpointId}' no longer matches its persisted dispatch snapshot.");
        }
        return provider;
    }

    private static void ValidatePolicy(ProductionDispatchGroup group)
    {
        if (!string.Equals(group.Policy.AdapterKey, Key, StringComparison.Ordinal))
            throw new InvalidOperationException($"RunPod dispatch policy must select '{Key}'.");
        if (group.Policy.SupportsNativeVariations)
            throw new InvalidOperationException("RunPod worker-comfyUI dispatch does not support native image variations.");
        if (group.Attempts.Count != 1)
            throw new InvalidOperationException("Each RunPod worker-comfyUI provider job must own exactly one immutable attempt.");
    }

    private static IReadOnlyList<ProductionProviderOutput> ParseRunPodOutputs(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output)
            || !output.TryGetProperty("images", out var images)
            || images.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Completed RunPod response did not contain output.images.");
        var results = new List<ProductionProviderOutput>();
        var ordinal = 0;
        foreach (var image in images.EnumerateArray())
        {
            var type = RequiredString(image, "type", "RunPod output image");
            if (!string.Equals(type, "base64", StringComparison.Ordinal))
                throw new InvalidOperationException($"RunPod output type '{type}' is not supported by the owned-storage reconciler.");
            results.Add(new ProductionProviderOutput(
                ordinal++, "image/png", RequiredString(image, "data", "RunPod output image"), null,
                JsonSerializer.Serialize(new { providerType = type })));
        }
        if (results.Count == 0) throw new InvalidOperationException("Completed RunPod response contained no images.");
        return results;
    }

    private static string StatusUri(ProductionProviderEndpoint endpoint, string providerRequestId) =>
        BuildUri(endpoint.BaseUrl, endpoint.StatusPathTemplate.Replace(
            "{jobId}", Uri.EscapeDataString(providerRequestId), StringComparison.Ordinal));

    private static string BuildUri(string baseUrl, string path) => $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static string RequiredString(JsonElement parent, string name, string label)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"{label} field '{name}' is required.");
        return value.GetString()!;
    }

    private static double? OptionalNumber(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;
}

public sealed class TogetherProductionDispatchAdapter : IProductionDispatchAdapter
{
    public const string Key = "together-images-v1";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProviderRepository _providers;
    private readonly IApiKeyEncryptionService _encryption;

    public TogetherProductionDispatchAdapter(
        IHttpClientFactory httpClientFactory,
        IProviderRepository providers,
        IApiKeyEncryptionService encryption)
    {
        _httpClientFactory = httpClientFactory;
        _providers = providers;
        _encryption = encryption;
    }

    public string AdapterKey => Key;

    public async Task<IReadOnlyList<ProductionProviderSubmission>> SubmitAsync(
        ProductionDispatchGroup group,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(group.Policy.AdapterKey, Key, StringComparison.Ordinal)
            || !group.Policy.SupportsNativeVariations)
            throw new InvalidOperationException("Together native variations require the explicit together-images-v1 policy.");
        if (group.Attempts.Count == 0 || group.Attempts.Count > group.Policy.MaximumOutputsPerRequest)
            throw new InvalidOperationException("Together variation count is outside the persisted capability limit.");
        if (group.Attempts.Select(attempt => attempt.Request.Id).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidOperationException("Together native variations can group only attempts for one compiled request.");
        var client = await CreateClientAsync(group.Endpoint, cancellationToken);
        var request = JsonNode.Parse(group.Attempts[0].Request.CanonicalProviderRequestJson) as JsonObject
            ?? throw new InvalidOperationException("Compiled Together request must be a JSON object.");
        request["n"] = group.Attempts.Count;
        using var response = await client.PostAsJsonAsync(
            BuildUri(group.Endpoint.BaseUrl, group.Endpoint.SubmitPath), request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Together image submission failed with HTTP {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var responseId = RequiredString(document.RootElement, "id", "Together image response");
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() != group.Attempts.Count)
            throw new InvalidOperationException("Together output count did not match the persisted variation attempts.");
        var results = new List<ProductionProviderSubmission>();
        for (var ordinal = 0; ordinal < group.Attempts.Count; ordinal++)
        {
            var output = data[ordinal];
            var base64 = OptionalString(output, "b64_json");
            var url = OptionalString(output, "url");
            if (string.IsNullOrWhiteSpace(base64) == string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Together output must contain exactly one of b64_json or url.");
            var attemptId = group.Attempts[ordinal].Attempt.Id;
            results.Add(new ProductionProviderSubmission(
                attemptId, $"{responseId}:variation:{ordinal}",
                $"{BuildUri(group.Endpoint.BaseUrl, group.Endpoint.SubmitPath)}#{Uri.EscapeDataString(responseId)}",
                ProductionProviderJobState.Succeeded,
                JsonSerializer.Serialize(new { id = responseId, variation = ordinal }),
                "{\"providerReported\":false}",
                [new ProductionProviderOutput(ordinal, "image/png", base64, url, "{}")]
            ));
        }
        return results;
    }

    public Task<ProductionProviderPollResult> PollAsync(
        ProductionProviderEndpoint endpoint,
        string providerRequestId,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Together Images returns results synchronously and has no qualified polling contract.");

    public Task CancelAsync(
        ProductionProviderEndpoint endpoint,
        string providerRequestId,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Together Images has no qualified cancellation contract after synchronous submission.");

    private async Task<HttpClient> CreateClientAsync(
        ProductionProviderEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(endpoint.ProtocolKey, Key, StringComparison.Ordinal))
            throw new InvalidOperationException($"Together endpoint protocol must be '{Key}'.");
        var provider = await _providers.GetByIdAsync(endpoint.EndpointId, cancellationToken)
            ?? throw new InvalidOperationException($"Configured provider endpoint '{endpoint.EndpointId}' was not found.");
        if (!provider.IsEnabled || provider.ProviderType != ProviderType.TogetherAI
            || provider.ImageProtocol != ImageProtocol.OpenAiImages
            || !string.Equals(provider.BaseUrl.TrimEnd('/'), endpoint.BaseUrl.TrimEnd('/'), StringComparison.Ordinal)
            || provider.TimeoutSeconds != endpoint.TimeoutSeconds
            || string.IsNullOrWhiteSpace(provider.ApiKeyEncrypted))
            throw new InvalidOperationException($"Provider endpoint '{endpoint.EndpointId}' no longer matches its persisted dispatch snapshot.");
        var apiKey = _encryption.Decrypt(provider.ApiKeyEncrypted);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"Provider '{provider.Id}' API key decrypted to an empty value.");
        var client = _httpClientFactory.CreateClient("CompletionClient");
        client.Timeout = TimeSpan.FromSeconds(endpoint.TimeoutSeconds);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static string BuildUri(string baseUrl, string path) => $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static string RequiredString(JsonElement parent, string name, string label)
    {
        var value = OptionalString(parent, name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"{label} field '{name}' is required.");
    }

    private static string? OptionalString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}