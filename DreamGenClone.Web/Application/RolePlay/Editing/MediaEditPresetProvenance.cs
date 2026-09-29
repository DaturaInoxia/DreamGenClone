using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// What a queued preset run records about itself: which preset was picked, and the checksum of the instruction it
/// assembled to (B-133).
///
/// <para>
/// The instruction TEXT is the queued row's own prompt, so it is not duplicated here. What this record adds is the
/// ability to re-derive that text and prove it is the same: a preset instruction is assembled deterministically from
/// store rows, so a run can recompute it and compare. That is the audit trail a compiled edit gets from its prompt
/// revision — and it matters for the same reason: a queued instruction whose wording changed underneath it must not
/// silently render something the operator never approved.
/// </para>
/// </summary>
public sealed record MediaEditPresetInstruction(
    string PresetKey,
    string InstructionSha256,
    string? CharacterId = null);

/// <summary>
/// The provenance contract of a preset run, mirroring <see cref="MediaEditIdentityProvenance"/>: an authored
/// instruction with no compiler artifact behind it. One writer shape and one reader, so a queued row cannot be
/// classified two different ways in two places.
/// </summary>
public static class MediaEditPresetProvenance
{
    /// <summary>The provenance key holding the picked preset and its instruction checksum.</summary>
    public const string PresetKeyName = "presetInstruction";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The checksum of an assembled instruction. ONE function, used by the service that queues a preset run and by
    /// the writer that re-derives it: two spellings of this would disagree the moment either side changed, and the
    /// disagreement would surface as every preset run refusing itself.
    /// </summary>
    public static string InstructionSha256(string instruction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instruction))).ToLowerInvariant();
    }

    /// <summary>
    /// Reads the preset a queued image carries, or null when the row is not a preset run. An unreadable or
    /// incomplete block is refused rather than treated as "not a preset run": the row claims to be one.
    /// </summary>
    public static MediaEditPresetInstruction? TryRead(string? provenanceJson)
    {
        if (string.IsNullOrWhiteSpace(provenanceJson))
        {
            return null;
        }

        JsonElement root;
        try
        {
            using var provenance = JsonDocument.Parse(provenanceJson);
            root = provenance.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "A queued edit row has unreadable provenance, so whether it is a preset run cannot be determined.", ex);
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(PresetKeyName, out var element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var preset = element.Deserialize<MediaEditPresetInstruction>(JsonOptions)
            ?? throw new InvalidOperationException("A queued preset edit has an unreadable preset provenance block.");
        Validate(preset);
        return preset;
    }

    /// <summary>Fails fast on a preset block that cannot drive a run, naming what is missing.</summary>
    public static void Validate(MediaEditPresetInstruction preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (string.IsNullOrWhiteSpace(preset.PresetKey))
        {
            throw new InvalidOperationException("A preset edit must record the preset it was assembled from.");
        }

        // The key names its axis, so an unknown key means the row was written by something that does not know this
        // contract - and the axis is what decides the preserve clause the run must carry.
        ImagePresetKeys.AxisOf(preset.PresetKey);

        if (string.IsNullOrWhiteSpace(preset.InstructionSha256) || preset.InstructionSha256.Trim().Length != 64)
        {
            throw new InvalidOperationException(
                $"The preset edit's instruction checksum '{preset.InstructionSha256}' is not a SHA-256 value, so the "
                + "instruction that will run cannot be proven to be the one that was assembled.");
        }
    }
}
