using System.Text.Json.Nodes;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Collapses byte-identical duplicate prose in a compiled Still brief's semantic snapshot before the text is handed
/// to the compile LLM.
/// </summary>
/// <remarks>
/// The snapshot repeats the same sentence across several leaves: the moment's frozen-state line is byte-identical to
/// the global continuity state, and the frozen location/lighting sentences are copied into both continuity blocks.
/// A compiler that reads the same fact three times expands it three ways, which is how an over-complicated prompt
/// comes back from a brief the operator never asked to be long. This strips only VERBATIM copies and always keeps the
/// canonical owner, so no meaning is removed and no value is invented. The persisted brief is never touched — only
/// the text sent to the compiler is reduced.
/// </remarks>
public static class SemanticSnapshotDeDuplicator
{
    /// <summary>
    /// Canonical owner first, then the leaves that are dropped when their text equals the owner's. The owner is kept
    /// even when its value is empty; a duplicate is dropped only when the owner's value is present and equal.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DuplicateLeaves =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["moment.frozenState"] = ["frozenState.continuityState"],
            ["frozenState.location"] = ["continuity.start.location", "continuity.end.location"],
            ["frozenState.lighting"] = ["continuity.start.lighting", "continuity.end.lighting"]
        };

    /// <summary>Returns the snapshot with verbatim duplicates removed, or the input unchanged when unreadable.</summary>
    public static string DeDuplicate(string semanticInputSnapshotJson)
    {
        ArgumentNullException.ThrowIfNull(semanticInputSnapshotJson);

        try
        {
            if (JsonNode.Parse(semanticInputSnapshotJson) is not JsonObject root)
            {
                return semanticInputSnapshotJson;
            }

            foreach (var (ownerPath, duplicates) in DuplicateLeaves)
            {
                var ownerText = ScalarAt(root, ownerPath);
                foreach (var duplicatePath in duplicates)
                {
                    var duplicateText = ScalarAt(root, duplicatePath);
                    if (ownerText is not null
                        && duplicateText is not null
                        && string.Equals(ownerText, duplicateText, StringComparison.Ordinal))
                    {
                        RemoveLeaf(root, duplicatePath);
                    }
                }
            }

            return root.ToJsonString();
        }
        catch (System.Text.Json.JsonException)
        {
            return semanticInputSnapshotJson;
        }
    }

    /// <summary>The scalar string at a dotted path, or null when absent or not a string.</summary>
    private static string? ScalarAt(JsonObject root, string path)
    {
        var segments = path.Split('.');
        JsonNode? current = root;
        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || obj[segment] is not { } next)
            {
                return null;
            }

            current = next;
        }

        return current is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    /// <summary>Removes the leaf named by a dotted path from its parent object.</summary>
    private static void RemoveLeaf(JsonObject root, string path)
    {
        var segments = path.Split('.');
        JsonNode? current = root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (current is not JsonObject obj || obj[segments[index]] is not { } next)
            {
                return;
            }

            current = next;
        }

        (current as JsonObject)?.Remove(segments[^1]);
    }
}
