using System.Security.Cryptography;
using System.Text;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Whether the prompt still matches the reference bindings it was drafted from (D5).
/// </summary>
/// <remarks>
/// Changing a binding does NOT silently regenerate the prompt: a draft the operator has edited must never be
/// overwritten behind their back, and an LLM call is not a side effect of moving a slot. Instead the bindings
/// change, the prompt is marked STALE with the reason, and regenerating stays an explicit action.
///
/// The signature covers ORDER as well as content, because order is request data - the first reference anchors the
/// frame, so reordering two references genuinely changes the render even though the same images are bound.
/// </remarks>
public static class StepPromptStaleness
{
    /// <summary>The signature of no bindings at all - a fully-textual step.</summary>
    public const string NoBindings = "none";

    /// <summary>
    /// A stable, compact signature of a step's bindings. Deterministic across processes (unlike a string hash), so a
    /// signature persisted or logged earlier still compares correctly.
    /// </summary>
    public static string SignatureFor(IReadOnlyList<ReferenceApplicationSelection>? bindings)
    {
        if (bindings is null || bindings.Count == 0)
        {
            return NoBindings;
        }

        var canonical = string.Join(
            "\n",
            bindings
                .OrderBy(binding => binding.Ordinal ?? int.MaxValue)
                .ThenBy(binding => binding.ElementKey, StringComparer.Ordinal)
                .Select(binding => string.Join(
                    "|",
                    binding.Ordinal?.ToString() ?? string.Empty,
                    binding.Kind ?? binding.ElementKey,
                    binding.ActorKey ?? string.Empty,
                    binding.Strategy,
                    binding.SceneAssetId ?? string.Empty,
                    binding.SceneAssetImageId ?? string.Empty,
                    binding.SceneAssetSha256 ?? string.Empty)));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }

    /// <summary>
    /// Whether the current bindings differ from the signature the prompt was drafted against. A null or empty
    /// signature means "not known" and is treated as NOT stale, so an unknown state never accuses the operator of
    /// having changed something they did not.
    /// </summary>
    public static bool IsStale(string? generatedForSignature, IReadOnlyList<ReferenceApplicationSelection>? bindings)
    {
        if (string.IsNullOrWhiteSpace(generatedForSignature))
        {
            return false;
        }

        return !string.Equals(generatedForSignature, SignatureFor(bindings), StringComparison.Ordinal);
    }

    /// <summary>
    /// What changed between the bindings a prompt was drafted from and the bindings now, in plain words. Returns an
    /// empty list when nothing did, so a caller can render an empty notice rather than a reassuring one.
    /// </summary>
    public static IReadOnlyList<string> DescribeChanges(
        IReadOnlyList<ReferenceApplicationSelection>? previous,
        IReadOnlyList<ReferenceApplicationSelection>? current)
    {
        var before = Key(previous);
        var after = Key(current);

        var changes = new List<string>();
        foreach (var (key, label) in after)
        {
            if (!before.ContainsKey(key))
            {
                changes.Add($"added {label}");
            }
        }

        foreach (var (key, label) in before)
        {
            if (!after.ContainsKey(key))
            {
                changes.Add($"removed {label}");
            }
        }

        foreach (var (key, label) in after)
        {
            if (before.TryGetValue(key, out var was) && !string.Equals(was, label, StringComparison.Ordinal))
            {
                // Same slot and position, different reference or strategy - say both, because "changed" alone does
                // not tell the operator whether the image or the mechanism moved.
                changes.Add($"changed {label} (previously {was})");
            }
        }

        return changes;
    }

    private static Dictionary<string, string> Key(IReadOnlyList<ReferenceApplicationSelection>? bindings) =>
        (bindings ?? [])
            .Select(binding => (Id: DescribeKey(binding), Label: DescribeBinding(binding)))
            .ToDictionary(entry => entry.Id, entry => entry.Label, StringComparer.Ordinal);

    private static string DescribeKey(ReferenceApplicationSelection binding) =>
        string.Join(
            "|",
            binding.Ordinal?.ToString() ?? string.Empty,
            binding.Kind ?? binding.ElementKey,
            binding.ActorKey ?? string.Empty);

    private static string DescribeBinding(ReferenceApplicationSelection binding)
    {
        var kind = binding.Kind ?? binding.ElementKey;
        var actor = string.IsNullOrWhiteSpace(binding.ActorKey) ? string.Empty : $" for '{binding.ActorKey}'";
        var ordinal = binding.Ordinal is { } value ? $"#{value} " : string.Empty;
        var image = string.IsNullOrWhiteSpace(binding.SceneAssetImageId) ? string.Empty : $" ({binding.SceneAssetImageId})";
        return $"{ordinal}{kind}{actor} via {binding.Strategy}{image}";
    }
}
