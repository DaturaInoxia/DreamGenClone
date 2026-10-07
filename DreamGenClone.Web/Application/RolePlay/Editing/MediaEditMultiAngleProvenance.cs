using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// What a queued multi-angle camera run records about itself: the picked pose (azimuth, elevation, distance) and the
/// checksum of the <c>&lt;sks&gt;</c> instruction it assembled to.
///
/// <para>
/// The instruction TEXT is the queued row's own prompt, so it is not duplicated here. What this record adds is the
/// ability to re-derive that text from the three enum values and prove it is the same — the same audit trail a preset
/// run gets, for the same reason: a queued instruction whose wording changed underneath it must not silently render
/// something the operator never approved.
/// </para>
/// </summary>
public sealed record MediaEditMultiAngleInstruction(
    MultiAngleAzimuth Azimuth,
    MultiAngleElevation Elevation,
    MultiAngleDistance Distance,
    string InstructionSha256);

/// <summary>
/// The provenance contract of a multi-angle run, mirroring <see cref="MediaEditPresetProvenance"/>: an authored
/// instruction with no compiler artifact behind it. One writer shape and one reader, so a queued row cannot be
/// classified two different ways in two places.
/// </summary>
public static class MediaEditMultiAngleProvenance
{
    /// <summary>The provenance key holding the picked pose and its instruction checksum.</summary>
    public const string MultiAngleKeyName = "multiAngleInstruction";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The checksum of an assembled instruction, identical to the preset run's checksum rule.</summary>
    public static string InstructionSha256(string instruction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instruction))).ToLowerInvariant();
    }

    /// <summary>
    /// Reads the multi-angle pose a queued image carries, or null when the row is not a multi-angle run. An unreadable
    /// or incomplete block is refused rather than treated as "not a multi-angle run": the row claims to be one.
    /// </summary>
    public static MediaEditMultiAngleInstruction? TryRead(string? provenanceJson)
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
                "A queued edit row has unreadable provenance, so whether it is a multi-angle run cannot be determined.", ex);
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(MultiAngleKeyName, out var element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var instruction = element.Deserialize<MediaEditMultiAngleInstruction>(JsonOptions)
            ?? throw new InvalidOperationException("A queued multi-angle edit has an unreadable multi-angle provenance block.");
        Validate(instruction);
        return instruction;
    }

    /// <summary>Fails fast on a multi-angle block that cannot drive a run, naming what is missing.</summary>
    public static void Validate(MediaEditMultiAngleInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        if (!Enum.IsDefined(instruction.Azimuth))
        {
            throw new InvalidOperationException($"The multi-angle edit's azimuth '{(int)instruction.Azimuth}' is not a known azimuth.");
        }
        if (!Enum.IsDefined(instruction.Elevation))
        {
            throw new InvalidOperationException($"The multi-angle edit's elevation '{(int)instruction.Elevation}' is not a known elevation.");
        }
        if (!Enum.IsDefined(instruction.Distance))
        {
            throw new InvalidOperationException($"The multi-angle edit's distance '{(int)instruction.Distance}' is not a known distance.");
        }

        if (string.IsNullOrWhiteSpace(instruction.InstructionSha256) || instruction.InstructionSha256.Trim().Length != 64)
        {
            throw new InvalidOperationException(
                $"The multi-angle edit's instruction checksum '{instruction.InstructionSha256}' is not a SHA-256 value, so the "
                + "instruction that will run cannot be proven to be the one that was assembled.");
        }
    }
}
