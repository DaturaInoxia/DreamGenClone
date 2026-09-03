using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;

namespace DreamGenClone.Infrastructure.Models;

public sealed class RunPodCharacterLoraTrainingDispatchAdapter : ICharacterLoraTrainingDispatchAdapter
{
    public const string Key = "runpod-serverless-lora-training-v1";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProviderRepository _providers;
    private readonly IApiKeyEncryptionService _encryption;

    public RunPodCharacterLoraTrainingDispatchAdapter(
        IHttpClientFactory httpClientFactory,
        IProviderRepository providers,
        IApiKeyEncryptionService encryption)
    {
        _httpClientFactory = httpClientFactory;
        _providers = providers;
        _encryption = encryption;
    }

    public string AdapterKey => Key;

    public async Task<CharacterLoraTrainingSubmission> SubmitAsync(
        CharacterLoraTrainingRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(request.Endpoint, cancellationToken);
        var nativeRequest = JsonNode.Parse(request.CanonicalProviderRequestJson)
            ?? throw new InvalidOperationException("LoRA training provider request JSON was null.");
        using var response = await client.PostAsJsonAsync(
            BuildUri(request.Endpoint.BaseUrl, request.Endpoint.SubmitPath),
            new JsonObject { ["input"] = nativeRequest }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"RunPod LoRA training submission failed with HTTP {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var providerRequestId = RequiredString(document.RootElement, "id", "RunPod LoRA training submission");
        return new CharacterLoraTrainingSubmission(
            providerRequestId,
            StatusUri(request.Endpoint, providerRequestId),
            JsonSerializer.Serialize(new { id = providerRequestId, status = "IN_QUEUE" }));
    }

    public async Task<CharacterLoraTrainingPollResult> PollAsync(
        CharacterLoraTrainingRequest request,
        string providerRequestId,
        CancellationToken cancellationToken = default)
    {
        var client = await CreateClientAsync(request.Endpoint, cancellationToken);
        using var response = await client.GetAsync(StatusUri(request.Endpoint, providerRequestId), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"RunPod LoRA training status failed with HTTP {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var status = RequiredString(document.RootElement, "status", "RunPod LoRA training status");
        var state = status switch
        {
            "IN_QUEUE" => CharacterLoraTrainingProviderState.Queued,
            "IN_PROGRESS" => CharacterLoraTrainingProviderState.Running,
            "COMPLETED" => CharacterLoraTrainingProviderState.Succeeded,
            "FAILED" or "TIMED_OUT" => CharacterLoraTrainingProviderState.Failed,
            "CANCELLED" => CharacterLoraTrainingProviderState.Cancelled,
            _ => throw new InvalidOperationException($"Unknown RunPod LoRA training status '{status}'.")
        };
        var snapshot = JsonSerializer.Serialize(new
        {
            id = providerRequestId,
            status,
            error = OptionalString(document.RootElement, "error"),
            executionTime = OptionalNumber(document.RootElement, "executionTime"),
            delayTime = OptionalNumber(document.RootElement, "delayTime")
        });
        if (state != CharacterLoraTrainingProviderState.Succeeded)
        {
            var diagnostic = OptionalString(document.RootElement, "error");
            return new CharacterLoraTrainingPollResult(
                state, snapshot, JsonSerializer.Serialize(new[] { new { status } }),
                "[]", "[]", "[]", null, null, null,
                state == CharacterLoraTrainingProviderState.Failed ? "runpod_training_failed" : null,
                state == CharacterLoraTrainingProviderState.Failed
                    ? diagnostic ?? $"RunPod LoRA training ended as {status}."
                    : null);
        }

        if (!document.RootElement.TryGetProperty("output", out var output)
            || output.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Completed RunPod LoRA training response did not contain output.");
        if (!output.TryGetProperty("artifact", out var artifact) || artifact.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Completed RunPod LoRA training response did not contain output.artifact.");
        var byteLength = RequiredInt64(artifact, "byteLength", "RunPod LoRA artifact");
        if (byteLength <= 0) throw new InvalidOperationException("RunPod LoRA artifact byteLength must be positive.");
        return new CharacterLoraTrainingPollResult(
            state, snapshot,
            RequiredJson(output, "statusHistory", JsonValueKind.Array, "RunPod LoRA training output"),
            RequiredJson(output, "logs", JsonValueKind.Array, "RunPod LoRA training output"),
            RequiredJson(output, "samples", JsonValueKind.Array, "RunPod LoRA training output"),
            RequiredJson(output, "checkpoints", JsonValueKind.Array, "RunPod LoRA training output"),
            RequiredString(artifact, "fileRelativePath", "RunPod LoRA artifact"),
            RequiredString(artifact, "sha256", "RunPod LoRA artifact"),
            byteLength, null, null);
    }

    private async Task<HttpClient> CreateClientAsync(
        CharacterLoraTrainingEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(endpoint.AdapterKey, Key, StringComparison.Ordinal))
            throw new InvalidOperationException($"RunPod LoRA training endpoint adapter must be '{Key}'.");
        var provider = await _providers.GetByIdAsync(endpoint.EndpointId, cancellationToken)
            ?? throw new InvalidOperationException($"Configured LoRA training provider endpoint '{endpoint.EndpointId}' was not found.");
        if (!provider.IsEnabled
            || !string.Equals(provider.Name, endpoint.ProviderKey, StringComparison.Ordinal)
            || !string.Equals(provider.BaseUrl.TrimEnd('/'), endpoint.BaseUrl.TrimEnd('/'), StringComparison.Ordinal)
            || provider.TimeoutSeconds != endpoint.TimeoutSeconds
            || string.IsNullOrWhiteSpace(provider.ApiKeyEncrypted))
            throw new InvalidOperationException($"LoRA training provider endpoint '{endpoint.EndpointId}' no longer matches its persisted dispatch snapshot.");
        var apiKey = _encryption.Decrypt(provider.ApiKeyEncrypted);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"LoRA training provider '{provider.Id}' API key decrypted to an empty value.");
        var client = _httpClientFactory.CreateClient("CompletionClient");
        client.Timeout = TimeSpan.FromSeconds(endpoint.TimeoutSeconds);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static string StatusUri(CharacterLoraTrainingEndpoint endpoint, string providerRequestId) =>
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

    private static long RequiredInt64(JsonElement parent, string name, string label)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt64(out var result))
            throw new InvalidOperationException($"{label} field '{name}' must be an integer.");
        return result;
    }

    private static string RequiredJson(JsonElement parent, string name, JsonValueKind kind, string label)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != kind)
            throw new InvalidOperationException($"{label} field '{name}' must be a {kind}.");
        return value.GetRawText();
    }

    private static string? OptionalString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? OptionalNumber(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;
}