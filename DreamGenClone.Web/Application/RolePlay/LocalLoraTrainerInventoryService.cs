using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>One base model the training host can train against, as the host measured it.</summary>
public sealed record LoraTrainerCheckpoint(string Name, long ByteLength, string Sha256, string Folder)
{
    /// <summary>
    /// Shown in the pick list: the size, because a name alone does not say whether it is the 7 GB or the 28 GB
    /// model, and the FOLDER, because the families disagree about where a base model lives - kohya loads SDXL and
    /// Pony bases from <c>checkpoints</c> and musubi loads a Krea 2 base from <c>diffusion_models</c>, so the folder
    /// is what tells two similarly named files apart. <see cref="Name"/> stays BARE on purpose: it is the id the
    /// trainer joins with its own folder, so folding the folder into it would produce a path that does not exist.
    /// </summary>
    public string Display => $"{Name}  ({ByteLength / 1024d / 1024d / 1024d:0.0} GB)  ·  {Folder}";
}

/// <summary>
/// What a training host is and what it has. <see cref="Reachable"/> false carries the reason rather than throwing:
/// a trainer that is switched off is an ordinary state of the world, and the page has to be able to say so.
/// </summary>
public sealed record LoraTrainerInventory(
    bool Reachable,
    string BaseUrl,
    string? Error,
    IReadOnlyList<LoraTrainerCheckpoint> Checkpoints,
    IReadOnlyDictionary<string, string> TrainerStack)
{
    public static LoraTrainerInventory Unreachable(string baseUrl, string error) =>
        new(false, baseUrl, error, [], new Dictionary<string, string>(StringComparer.Ordinal));
}

public interface ILocalLoraTrainerInventoryService
{
    /// <summary>
    /// Ask a training host what it can train against, and with which stack.
    ///
    /// The first call makes the host hash every checkpoint it has (a 7 GB file takes about a minute), because a
    /// training profile has to record the exact checksum of the model it was qualified for. The host caches it.
    /// </summary>
    Task<LoraTrainerInventory> DescribeAsync(string baseUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the training host's inventory over the same HTTP surface the dispatch adapter trains through.
///
/// <para>
/// This is what makes the profile form a set of pick lists: the checkpoint names and their SHA-256 values are facts
/// of the host's disk, so the app asks for them instead of inviting an operator to type a 64-character checksum that
/// nothing validates against the file.
/// </para>
/// </summary>
public sealed class LocalLoraTrainerInventoryService : ILocalLoraTrainerInventoryService
{
    /// <summary>Long: the host hashes every checkpoint on the first call. Cached there afterwards.</summary>
    private const int FirstCallTimeoutSeconds = 900;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LocalLoraTrainerInventoryService> _logger;

    public LocalLoraTrainerInventoryService(
        IHttpClientFactory httpClientFactory,
        ILogger<LocalLoraTrainerInventoryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<LoraTrainerInventory> DescribeAsync(
        string baseUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "A training host is required. Pick the provider whose BaseUrl is the training service on the GPU host.");
        }

        var trimmed = baseUrl.TrimEnd('/');
        var client = _httpClientFactory.CreateClient(nameof(LocalLoraTrainerInventoryService));
        client.Timeout = TimeSpan.FromSeconds(FirstCallTimeoutSeconds);

        try
        {
            var checkpoints = await ReadCheckpointsAsync(client, trimmed, cancellationToken);
            var stack = await ReadTrainerStackAsync(client, trimmed, cancellationToken);
            return new LoraTrainerInventory(true, trimmed, null, checkpoints, stack);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException
            or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Training host inventory failed for {BaseUrl}.", trimmed);
            return LoraTrainerInventory.Unreachable(trimmed, exception.Message);
        }
    }

    private static async Task<IReadOnlyList<LoraTrainerCheckpoint>> ReadCheckpointsAsync(
        HttpClient client, string baseUrl, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{baseUrl}/checkpoints", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The training host answered {(int)response.StatusCode} for its checkpoint list: {body}");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("checkpoints", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The training host did not return a 'checkpoints' array.");
        }

        var checkpoints = new List<LoraTrainerCheckpoint>();
        foreach (var entry in entries.EnumerateArray())
        {
            var name = entry.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            var sha = entry.TryGetProperty("sha256", out var shaValue) ? shaValue.GetString() : null;
            var folder = entry.TryGetProperty("folder", out var folderValue) ? folderValue.GetString() : null;
            var length = entry.TryGetProperty("byteLength", out var lengthValue)
                && lengthValue.TryGetInt64(out var parsed)
                    ? parsed
                    : 0;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(sha) || length <= 0
                || string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidOperationException(
                    "A base model from the training host was missing its name, folder, size or checksum; "
                    + "a profile cannot be created from a partially described model.");
            }

            checkpoints.Add(new LoraTrainerCheckpoint(name, length, sha, folder));
        }

        return checkpoints;
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadTrainerStackAsync(
        HttpClient client, string baseUrl, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{baseUrl}/train/health", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The training host answered {(int)response.StatusCode} for its health: {body}");
        }

        using var document = JsonDocument.Parse(body);
        var stack = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("trainer", out var trainer)
            && trainer.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in trainer.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    stack[property.Name] = property.Value.GetString()!;
                }
            }
        }

        return stack;
    }
}
