using System.Net.Http.Headers;
using System.Text.Json;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.ModelManager;

public sealed class ModelMetadataService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<ModelMetadataService> _logger;

    public ModelMetadataService(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<ModelMetadataService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <summary>Query the provider's /v1/models endpoint to retrieve metadata for a specific model.</summary>
    public async Task<ModelMetadata?> FetchModelMetadataAsync(Provider provider, string modelIdentifier, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            var baseUrl = provider.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Min(provider.TimeoutSeconds, 30));

            if (!string.IsNullOrEmpty(provider.ApiKeyEncrypted))
            {
                var decryptedKey = _encryptionService.Decrypt(provider.ApiKeyEncrypted);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedKey);
            }

            _logger.LogInformation("Fetching model metadata for {ModelId} from {ProviderName} ({BaseUrl})",
                modelIdentifier, provider.Name, provider.BaseUrl);

            // Try /v1/models/{id} first (supported by OpenRouter, Together AI)
            var metadata = await TryFetchSingleModelAsync(client, modelIdentifier, cancellationToken);
            if (metadata is not null)
                return metadata;

            // Fall back to listing all models and finding the match
            metadata = await TryFetchFromModelsListAsync(client, modelIdentifier, cancellationToken);
            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch model metadata for {ModelId} from {ProviderName}",
                modelIdentifier, provider.Name);
            return null;
        }
    }

    private async Task<ModelMetadata?> TryFetchSingleModelAsync(HttpClient client, string modelIdentifier, CancellationToken cancellationToken)
    {
        try
        {
            var encodedId = Uri.EscapeDataString(modelIdentifier);
            using var response = await client.GetAsync($"v1/models/{encodedId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            return ParseModelObject(doc.RootElement, modelIdentifier, json);
        }
        catch
        {
            return null;
        }
    }

    private async Task<ModelMetadata?> TryFetchFromModelsListAsync(HttpClient client, string modelIdentifier, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync("v1/models", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var modelElement in dataArray.EnumerateArray())
            {
                var id = modelElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                if (string.Equals(id, modelIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    var modelJson = modelElement.GetRawText();
                    return ParseModelObject(modelElement, modelIdentifier, modelJson);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private ModelMetadata ParseModelObject(JsonElement element, string modelIdentifier, string rawJson)
    {
        var metadata = new ModelMetadata
        {
            ModelId = modelIdentifier,
            RawProviderData = rawJson.Length > 2000 ? rawJson[..2000] : rawJson
        };

        // Context window — different providers use different field names
        metadata.ContextWindowSize = ExtractContextWindow(element);

        // Architecture / description
        if (element.TryGetProperty("architecture", out var arch))
        {
            if (arch.ValueKind == JsonValueKind.Object)
            {
                metadata.Architecture = arch.GetRawText();
            }
            else if (arch.ValueKind == JsonValueKind.String)
            {
                metadata.Architecture = arch.GetString() ?? "";
            }
        }

        if (element.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String)
            metadata.Description = desc.GetString() ?? "";

        // Parameter count — OpenRouter provides this in top_provider or name patterns
        metadata.ParameterCount = ExtractParameterCount(element, modelIdentifier);

        _logger.LogInformation(
            "Parsed model metadata for {ModelId}: ContextWindow={ContextWindow}, Params={Params}, Arch={Arch}",
            modelIdentifier, metadata.ContextWindowSize, metadata.ParameterCount, metadata.Architecture);

        return metadata;
    }

    private static int ExtractContextWindow(JsonElement element)
    {
        // OpenRouter: context_length at root
        if (element.TryGetProperty("context_length", out var ctxLen) && ctxLen.TryGetInt32(out var ctx))
            return ctx;

        // Together AI: context_length at root
        if (element.TryGetProperty("context_window", out var ctxWin) && ctxWin.TryGetInt32(out var cw))
            return cw;

        // OpenAI-compatible: sometimes in model_spec or nested
        if (element.TryGetProperty("max_model_len", out var maxLen) && maxLen.TryGetInt32(out var ml))
            return ml;

        // LM Studio: check loaded model info
        if (element.TryGetProperty("max_context_length", out var maxCtx) && maxCtx.TryGetInt32(out var mc))
            return mc;

        // Together AI: top_provider.context_length
        if (element.TryGetProperty("top_provider", out var topProv) && topProv.ValueKind == JsonValueKind.Object)
        {
            if (topProv.TryGetProperty("context_length", out var tpCtx) && tpCtx.TryGetInt32(out var tpc))
                return tpc;
            if (topProv.TryGetProperty("max_completion_tokens", out var tpMax) && tpMax.TryGetInt32(out var tpm))
                return tpm;
        }

        return 0;
    }

    private static string ExtractParameterCount(JsonElement element, string modelIdentifier)
    {
        // OpenRouter: sometimes in architecture.num_parameters or top_provider
        if (element.TryGetProperty("architecture", out var arch) && arch.ValueKind == JsonValueKind.Object)
        {
            if (arch.TryGetProperty("num_parameters", out var numParams))
            {
                if (numParams.TryGetInt64(out var np) && np > 0)
                {
                    return np >= 1_000_000_000
                        ? $"{np / 1_000_000_000.0:F1}B"
                        : $"{np / 1_000_000.0:F0}M";
                }
            }
        }

        // Try to extract from model identifier name (e.g., "llama-3.1-8b-instruct", "qwen2.5-72b")
        var match = System.Text.RegularExpressions.Regex.Match(
            modelIdentifier,
            @"(\d+x)?(\d+\.?\d*)\s*[bB]\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success)
        {
            var prefix = match.Groups[1].Success ? match.Groups[1].Value : "";
            return $"{prefix}{match.Groups[2].Value}B";
        }

        return "";
    }
}
