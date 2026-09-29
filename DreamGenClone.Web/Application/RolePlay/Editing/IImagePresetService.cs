using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>One pickable preset, as a surface shows it.</summary>
public sealed record ImagePresetChoice(
    string PresetKey,
    string ShortName,
    ImagePresetAxis Axis,
    string Detail);

/// <summary>
/// Resolves a preset key into the text a model is sent (B-133): the detail from the store, the preserve clause for
/// its axis, and the assembly template for the mode - composed deterministically.
///
/// <para>
/// This is the one place a preset becomes an instruction. A surface picks a key; it never assembles wording itself,
/// and it never hands a preset to the vision compiler. Composition is deterministic, so the same key always produces
/// the same instruction - which is what lets a queued run re-derive it and compare checksums.
/// </para>
/// </summary>
public interface IImagePresetService
{
    /// <summary>
    /// The instruction (or compose clause) for a preset key. <paramref name="characterId"/> selects a character's
    /// override row when it has one, exactly as every other store read does.
    /// </summary>
    Task<string> ResolveInstructionAsync(
        string presetKey,
        ImagePresetMode mode = ImagePresetMode.Change,
        string? characterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Every preset of an axis (or all presets when <paramref name="axis"/> is null), in seed order.</summary>
    Task<IReadOnlyList<ImagePresetChoice>> ListAsync(
        ImagePresetAxis? axis = null,
        string? characterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The preset a LoRA cell's own axis value selects — the host default. Refuses an axis with no preset rather
    /// than returning something adjacent, because a plausible-looking key would be a hidden fallback.
    /// </summary>
    string DefaultPresetFor(string loraVocabularyKey);
}
