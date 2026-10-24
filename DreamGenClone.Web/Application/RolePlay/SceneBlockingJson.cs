using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Reads the frame-space <see cref="SceneBlocking"/> back out of a compiled brief's
/// <c>SemanticInputSnapshotJson</c> (B-157).
///
/// <para>
/// This is the exact inverse of what the compiler wrote, with the compiler's own serializer settings, so a render
/// describes its references with the framing that brief was actually compiled with rather than one derived later.
/// The member is absent on a brief compiled before this feature existed, on a non-still brief, and on a brief whose
/// caller supplied no blocking — all of which legitimately mean "no frame was derived", and all of which fall back to
/// the established scene-level clause. That is an optional input being absent, not a configured value being
/// substituted.
/// </para>
/// </summary>
public static class SceneBlockingJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>The brief's blocking, or null when it carries none.</summary>
    public static SceneBlocking? TryRead(string? semanticInputSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(semanticInputSnapshotJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(semanticInputSnapshotJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!TryGetBlocking(document.RootElement, out var blocking) || blocking.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var read = blocking.Deserialize<SceneBlocking>(Options);
            return read is { Subjects.Count: > 0 } ? read : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetBlocking(JsonElement root, out JsonElement blocking)
    {
        // The compiler writes camelCase; accept the PascalCase spelling too so a hand-inspected or re-serialized
        // snapshot cannot silently lose the frame.
        return root.TryGetProperty("blocking", out blocking) || root.TryGetProperty("Blocking", out blocking);
    }

    /// <summary>
    /// The same snapshot with its <c>blocking</c> member replaced by <paramref name="blocking"/> (B-161).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The brief's snapshot is the SINGLE semantic source handed to the prompt-writing model, and that model reads the
    /// frame straight out of it — which is how "The camera sits at eye level in a medium shot" was written into a
    /// prompt that had to show two people far apart (reported 2026-10-13). Appending a correct framing BELOW a
    /// snapshot that still states the old one would leave one prompt asserting two cameras, so the snapshot itself is
    /// made to carry the frame the render will actually use.
    /// </para>
    ///
    /// <para>
    /// Only the <c>blocking</c> member is touched: every other member is preserved exactly as the compiler wrote it,
    /// because this is the brief the render's own provenance points at. A snapshot with no <c>blocking</c> member is
    /// returned unchanged — there is no frame to correct, so inventing one would be the substitution this codebase
    /// forbids. The injected value is written camelCase, matching the compiler's own serializer.
    /// </para>
    /// </remarks>
    public static string Replace(string? semanticInputSnapshotJson, SceneBlocking? blocking)
    {
        if (string.IsNullOrWhiteSpace(semanticInputSnapshotJson) || blocking is null)
        {
            return semanticInputSnapshotJson ?? string.Empty;
        }

        try
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(semanticInputSnapshotJson);
            if (root is not System.Text.Json.Nodes.JsonObject snapshot
                || !TryGetBlockingMemberName(snapshot, out var memberName))
            {
                return semanticInputSnapshotJson;
            }

            snapshot[memberName] = System.Text.Json.Nodes.JsonNode.Parse(
                JsonSerializer.Serialize(blocking, Options));
            return snapshot.ToJsonString(Options);
        }
        catch (JsonException)
        {
            // A snapshot that cannot be parsed is left exactly as it is: the render's append path still carries the
            // effective frame, and rewriting a brief this code cannot read would be worse than not rewriting it.
            return semanticInputSnapshotJson;
        }
    }

    /// <summary>
    /// The same snapshot with its <c>blocking</c> member removed.
    /// </summary>
    /// <remarks>
    /// A participant POV is an egocentric camera: the brief's blocking records where each subject sits in an EXTERNAL
    /// frame, so handing it to the prompt writer alongside a first-person projection would assert two cameras at once —
    /// the contradiction that made every POV render as the same wide shot. The brief keeps its blocking for provenance
    /// and for the Omniscient POV; only the participant-facing prompt removes it, and the projection block then states
    /// the frame that actually applies.
    /// </remarks>
    public static string WithoutBlocking(string? semanticInputSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(semanticInputSnapshotJson))
        {
            return semanticInputSnapshotJson ?? string.Empty;
        }

        try
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(semanticInputSnapshotJson);
            if (root is not System.Text.Json.Nodes.JsonObject snapshot
                || !TryGetBlockingMemberName(snapshot, out var memberName))
            {
                return semanticInputSnapshotJson;
            }

            snapshot.Remove(memberName);
            return snapshot.ToJsonString(Options);
        }
        catch (JsonException)
        {
            return semanticInputSnapshotJson;
        }
    }

    private static bool TryGetBlockingMemberName(
        System.Text.Json.Nodes.JsonObject snapshot,
        out string memberName)
    {
        foreach (var name in snapshot.Select(member => member.Key))
        {
            if (string.Equals(name, "blocking", StringComparison.OrdinalIgnoreCase))
            {
                memberName = name;
                return true;
            }
        }

        memberName = string.Empty;
        return false;
    }
}
