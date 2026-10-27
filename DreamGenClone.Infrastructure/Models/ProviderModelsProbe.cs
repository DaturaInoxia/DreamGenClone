using System.Text.Json;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// Non-billable model-listing probe shared by every health check.
/// <para>
/// A health check must never produce inference: no chat completion, no image render, no video render.
/// Asking a provider for its model list costs nothing, and sending the key on that call still proves whether
/// the key is accepted (<c>401</c>/<c>403</c>). When a provider exposes no listing endpoint the check degrades
/// to a reachability ping and says so: the key and model are then unverified by design, and a bad key or an
/// empty credit balance surfaces as an ordinary generation failure when the user actually generates.
/// </para>
/// </summary>
internal static class ProviderModelsProbe
{
    /// <summary>Outcome of a model-listing request. <see cref="ListingSupported"/> is false for 404/405.</summary>
    internal readonly record struct Result(int StatusCode, bool ListingSupported, bool ContainsModel);

    /// <summary>
    /// Turns any provider endpoint path into the sibling model-listing path, keeping the same API root:
    /// <c>/v1/images/generations</c> → <c>/v1/models</c>; <c>/v1/chat/completions</c> → <c>/v1/models</c>.
    /// </summary>
    public static string ModelsPath(string? endpointPath)
    {
        var path = (endpointPath ?? string.Empty).Trim();
        if (path.Length == 0)
        {
            return "/v1/models";
        }

        var queryStart = path.IndexOfAny(['?', '#']);
        if (queryStart >= 0)
        {
            path = path[..queryStart];
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Strip the endpoint's own trailing resource segments so only the API root survives, e.g.
        // ["v1","images","generations"] → ["v1"] and ["api","v1","chat","completions"] → ["api","v1"].
        while (segments.Count > 0 && IsResourceSegment(segments[^1]))
        {
            segments.RemoveAt(segments.Count - 1);
        }

        return segments.Count == 0
            ? "/v1/models"
            : "/" + string.Join('/', segments) + "/models";
    }

    private static bool IsResourceSegment(string segment)
        => segment.ToLowerInvariant() is
            "generations" or "completions" or "edits" or "images" or "chat" or "embeddings" or "responses" or "models";

    /// <summary>
    /// GETs the provider's model list and reports the status plus whether it names <paramref name="modelIdentifier"/>.
    /// Performs no inference of any kind.
    /// </summary>
    public static async Task<Result> QueryAsync(
        HttpClient client,
        string endpointPath,
        string modelIdentifier,
        CancellationToken cancellationToken)
    {
        var relativePath = ModelsPath(endpointPath).TrimStart('/');
        using var response = await client.GetAsync(relativePath, cancellationToken);
        var statusCode = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            // 404/405 means the provider simply has no listing route; anything else is a real answer about the key.
            return new Result(statusCode, statusCode is not (404 or 405), ContainsModel: false);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var ids = ParseModelIds(body);

        // An empty or unrecognised payload cannot disprove the model's availability, so treat it as a pass.
        var containsModel = ids.Count == 0
            || string.IsNullOrWhiteSpace(modelIdentifier)
            || ids.Any(id => Matches(id, modelIdentifier));

        return new Result(statusCode, ListingSupported: true, containsModel);
    }

    private static bool Matches(string listedId, string modelIdentifier)
        => string.Equals(listedId, modelIdentifier, StringComparison.OrdinalIgnoreCase)
           || listedId.EndsWith("/" + modelIdentifier, StringComparison.OrdinalIgnoreCase)
           || modelIdentifier.EndsWith("/" + listedId, StringComparison.OrdinalIgnoreCase);

    private static List<string> ParseModelIds(string body)
    {
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return ids;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            // OpenAI-compatible providers answer {"data":[{"id":...}]}; Ollama-style servers answer {"models":[...]}.
            if (!TryGetArray(root, "data", out var items) && !TryGetArray(root, "models", out items))
            {
                return ids;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    ids.Add(item.GetString()!);
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var property in (string[])["id", "name", "model"])
                {
                    if (item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        ids.Add(value.GetString()!);
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON payload: no ids to compare against, callers treat this as "cannot disprove".
        }

        return ids;
    }

    private static bool TryGetArray(JsonElement root, string property, out JsonElement array)
    {
        if (root.TryGetProperty(property, out array) && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        array = default;
        return false;
    }
}
