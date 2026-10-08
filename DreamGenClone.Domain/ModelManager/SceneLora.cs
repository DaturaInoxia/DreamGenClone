namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// What a scene LoRA is FOR. It groups the picker and it is the audit label, not a filter: compatibility is
/// decided by <see cref="SceneLora.SceneImageModelFamily"/>, never by category. Appended, never renumbered.
/// </summary>
public enum SceneLoraCategory
{
    Unknown = 0,

    /// <summary>Unlocks or restores explicit/NSFW capability the base model restricts.</summary>
    Unlock = 1,

    /// <summary>Teaches one specific sexual act or position (the grounded per-act LoKrs).</summary>
    Act = 2,

    /// <summary>Improves a body region's fidelity (anatomy helpers).</summary>
    Anatomy = 3,

    /// <summary>Steers look, rendering or realism rather than content.</summary>
    Style = 4
}

/// <summary>
/// One row of the scene-LoRA catalog: the single, persisted source of which NON-IDENTITY LoRAs exist and which
/// model family each one belongs to.
///
/// <para>
/// This is the storage spot for Krea 2's model LoRAs (unlock, the grounded act LoKrs, anatomy and realism
/// helpers) and, per family, for every other family's. A LoRA only binds to the checkpoint it was trained
/// against, so the family is what filters the operator's multi-select picker: a render can only offer LoRAs whose
/// family matches the selected model's.
/// </para>
///
/// <para>
/// This is NOT the character-LoRA store. Identity LoRAs live in <c>CharacterLoraArtifact</c> (a different table,
/// carrying a character, a trigger token and a checksum) and are untouched by this catalog. The separation is
/// deliberate: a scene LoRA has no character, and a character LoRA must never be re-pointed by a scene picker.
/// </para>
///
/// <para>
/// Nothing here is force-applied. The catalog only describes what MAY be picked; a render with no selection emits
/// no loader node at all.
/// </para>
/// </summary>
public sealed class SceneLora
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The LoRA filename as ComfyUI knows it (what <c>lora_name</c> receives), e.g.
    /// <c>krea2_nsfw_v4_v43exp.safetensors</c>.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The label the picker shows, e.g. "Krea2 NSFW V4".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The model family this LoRA may be applied to. The picker lists a LoRA only when the selected model's family
    /// matches this value, and resolution refuses a selection whose family does not match the render's model.
    /// </summary>
    public SceneImageModelFamily SceneImageModelFamily { get; set; }

    /// <summary>What the LoRA is for. Groups the picker; never used to decide compatibility.</summary>
    public SceneLoraCategory Category { get; set; }

    /// <summary>
    /// The strength the operator's picker starts at. Required and positive: a LoRA applied at a strength nobody
    /// chose is a different LoRA, so a catalog row with no strength is refused rather than guessed.
    /// </summary>
    public double DefaultStrength { get; set; }

    /// <summary>
    /// Whether the row may be offered. A disabled row stays in the catalog (its provenance is not deleted) but is
    /// never listed by the picker or resolvable by a render.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// The token this LoRA needs carried into the prompt, when it has one (for example the fal realism LoRA's
    /// <c>r34l1sm</c>). It belongs to the LoRA, not to a render: the compiler reads it off the selected stack, and
    /// the picker shows it, so an operator who selects the LoRA sees that its trigger is already handled. Null or
    /// empty means the LoRA has no trigger, which is a configured state and not a missing value.
    /// </summary>
    public string? TriggerToken { get; set; }

    /// <summary>Optional operator note (what the LoRA was proven to do, and how).</summary>
    public string? Notes { get; set; }
}
