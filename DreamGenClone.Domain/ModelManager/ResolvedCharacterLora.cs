namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// One character LoRA this render applies: the trained artifact, the file ComfyUI loads, the strength, and the
/// trigger token that must appear in the prompt for the LoRA to bind to THIS character.
///
/// Immutable, and carried on <see cref="ResolvedImageModel"/> so the graph builder, the audit event and the
/// resolved-model badge all describe the same render. Identity strategies are siblings, not replacements: a
/// character with no qualified LoRA keeps its reference/IP-Adapter route untouched, and a render with no LoRAs
/// emits no <c>LoraLoader</c> node at all.
/// </summary>
/// <param name="ArtifactId">The <c>CharacterLoraArtifact</c> row this render used.</param>
/// <param name="CharacterProfileId">The character whose identity this LoRA carries.</param>
/// <param name="CharacterName">The character's display name, so a failure names a person rather than an id.</param>
/// <param name="TriggerToken">The token prepended to this character's prompt; without it the LoRA does not bind.</param>
/// <param name="FileName">The LoRA filename as ComfyUI knows it (what <c>lora_name</c> receives).</param>
/// <param name="Strength">Model and clip strength. Explicit, never defaulted.</param>
/// <param name="Sha256">The artifact checksum, recorded so a render can be reproduced exactly.</param>
public sealed record ResolvedCharacterLora(
    string ArtifactId,
    string CharacterProfileId,
    string CharacterName,
    string TriggerToken,
    string FileName,
    double Strength,
    string Sha256);
