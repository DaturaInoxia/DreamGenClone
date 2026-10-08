namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// One NON-IDENTITY LoRA a render applies: an unlock, act, anatomy or style LoRA picked by the operator from the
/// scene-LoRA catalog, with the strength they chose.
///
/// <para>
/// Deliberately NOT <see cref="ResolvedCharacterLora"/>. A character LoRA carries an artifact id, a character, a
/// trigger token and a checksum because it IS an identity; a scene LoRA carries none of those, and giving it a
/// character-shaped record would let an act LoRA masquerade as a person. The graph builder chains scene LoRAs
/// first and character LoRAs last, so identity stays closest to the subject.
/// </para>
///
/// <para>
/// Immutable, and carried on <see cref="ResolvedImageModel"/> alongside the character LoRAs. A render with no
/// scene-LoRA selection carries null/empty and emits no loader node, so it is byte-identical to the graph the
/// family builder produced before scene LoRAs existed.
/// </para>
/// </summary>
/// <param name="FileName">The LoRA filename as ComfyUI knows it (what <c>lora_name</c> receives).</param>
/// <param name="Strength">Model and clip strength. Explicit, never defaulted.</param>
/// <param name="Purpose">
/// The catalog's human-readable purpose (for example "Krea2 NSFW unlock", "Cowgirl act LoKr"), recorded so an
/// audit event can say WHY a LoRA was in the chain without a second lookup.
/// </param>
/// <param name="TriggerToken">
/// The token the catalog row requires in the prompt, when it has one (the fal realism LoRA's <c>r34l1sm</c>).
/// Carried so a compiler can place it without a second catalog lookup; null means the LoRA has no trigger.
/// </param>
public sealed record ResolvedSceneLora(
    string FileName,
    double Strength,
    string? Purpose = null,
    string? TriggerToken = null);
