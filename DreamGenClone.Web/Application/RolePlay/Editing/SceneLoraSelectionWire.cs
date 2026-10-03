using System.Text.Json;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// The ONE owner of the scene-LoRA selection's wire form on a queued edit (B-143).
///
/// <para>
/// Both stores queue the same selection onto the same job payload — the role-play scene image and the Asset Manager
/// asset image — and ONE handler reads it for both, so the shape is defined once here rather than written in each
/// caller and read a third way. The selection is carried as JSON, not re-resolved at run time, for the same reason
/// reference applications are: what the operator picked must reach the worker unchanged.
/// </para>
///
/// <para>
/// Absent means NO scene LoRA. That is a configured state, and it must stay distinguishable from a selection that
/// could not be read: malformed JSON THROWS, because a run that quietly rendered without the stack the operator
/// picked produces an image nobody asked for and looks exactly like one that applied it.
/// </para>
/// </summary>
public static class SceneLoraSelectionWire
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializes the operator's selection, or returns null when there is none — so a payload carries NO stack
    /// rather than an empty one, which is the same "absent means none" contract the compose path uses.
    /// </summary>
    public static string? Serialize(IReadOnlyList<SceneImageLoraSelection>? selections) =>
        selections is { Count: > 0 } ? JsonSerializer.Serialize(selections, JsonOptions) : null;

    /// <summary>
    /// Reads the selection a run was queued with. Absent or blank is no selection; malformed JSON is a payload
    /// defect and fails the run.
    /// </summary>
    public static IReadOnlyList<SceneImageLoraSelection> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var selections = JsonSerializer.Deserialize<List<SceneImageLoraSelection>>(json, JsonOptions)
            ?? throw new InvalidOperationException(
                "The queued edit carries a scene-LoRA selection that could not be read, so the run cannot apply the "
                + "stack the operator picked.");
        return selections;
    }
}
