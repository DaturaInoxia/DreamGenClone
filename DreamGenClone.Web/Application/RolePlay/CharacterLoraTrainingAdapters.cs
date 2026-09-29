using DreamGenClone.Infrastructure.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The training adapters the app can dispatch to, and the paths each one expects.
///
/// <para>
/// The paths are a property of the adapter, not of the operator: a RunPod serverless worker answers on
/// <c>/run</c> and a local kohya service answers on <c>/train</c>, and asking for them one box at a time invited a
/// solved path to be retyped wrongly. They live here beside the adapter keys so the two cannot drift.
/// </para>
/// </summary>
public static class CharacterLoraTrainingAdapters
{
    public sealed record Entry(
        string AdapterKey,
        string Label,
        string SubmitPath,
        string StatusPath,
        string CancelPath,
        string Note);

    public static readonly IReadOnlyList<Entry> All =
    [
        new Entry(
            LocalCharacterLoraTrainingDispatchAdapter.Key,
            "Local kohya trainer (the GPU host that runs ComfyUI)",
            "/train",
            "/train/{jobId}",
            "/train/{jobId}/cancel",
            "Sends the dataset to the training service on the ComfyUI host and polls it. The LoRA is published into "
            + "that host's ComfyUI loras folder, which is the runtime that will use it."),

        new Entry(
            RunPodCharacterLoraTrainingDispatchAdapter.Key,
            "RunPod serverless trainer",
            "/run",
            "/status/{jobId}",
            "/cancel/{jobId}",
            "Sends the dataset to a RunPod serverless endpoint and polls it. Needs an endpoint id and an API key on "
            + "the provider row.")
    ];

    public static Entry? Find(string? adapterKey) => adapterKey is null
        ? null
        : All.FirstOrDefault(entry => string.Equals(entry.AdapterKey, adapterKey.Trim(), StringComparison.Ordinal));

    /// <summary>
    /// The adapter to offer first: whichever one the selected provider can actually serve.
    ///
    /// A RunPod provider answers on a runpod.net host and a local trainer answers on the LAN, so the address is
    /// enough to pick sensibly without asking again.
    /// </summary>
    public static Entry DefaultFor(string? providerBaseUrl)
    {
        var looksRemote = providerBaseUrl is not null
            && providerBaseUrl.Contains("runpod", StringComparison.OrdinalIgnoreCase);
        return looksRemote
            ? All.First(entry => entry.AdapterKey == RunPodCharacterLoraTrainingDispatchAdapter.Key)
            : All.First(entry => entry.AdapterKey == LocalCharacterLoraTrainingDispatchAdapter.Key);
    }
}
