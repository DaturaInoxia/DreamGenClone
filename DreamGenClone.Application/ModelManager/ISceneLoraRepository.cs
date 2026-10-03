using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.ModelManager;

/// <summary>
/// Reads the scene-LoRA catalog (B-137 §4): the persisted rows that say which non-identity LoRAs exist and which
/// model family each one belongs to.
///
/// <para>
/// Read-only by design. The catalog is seeded and edited as data (through the DbQuery tool), and the app only ever
/// lists it - a render must never invent a LoRA row, and a picker must never write one.
/// </para>
///
/// <para>
/// This is NOT the character-LoRA store. Identity LoRAs are resolved through
/// <c>ICharacterLoraRepository</c> from the qualified artifacts trained against the render's checkpoint; a scene
/// LoRA has no character, no trigger token and no checksum.
/// </para>
/// </summary>
public interface ISceneLoraRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The enabled catalog rows a render on <paramref name="family"/> may pick from, ordered for display. A model
    /// family with no rows gets an empty list, which is a configured state (nothing is offered), not an error.
    /// </summary>
    Task<IReadOnlyList<SceneLora>> ListAsync(
        SceneImageModelFamily family, CancellationToken cancellationToken = default);

    /// <summary>
    /// One catalog row by its ComfyUI filename, or null when no such row exists. The render resolver uses this to
    /// refuse a selection that names a LoRA the catalog does not carry.
    /// </summary>
    Task<SceneLora?> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default);
}
