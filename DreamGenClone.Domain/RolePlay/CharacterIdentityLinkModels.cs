namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// An explicit, human-made link from a character <b>instance</b> — a scenario character or a character asset — to
/// the character <b>template</b> that owns its identity (B-127).
///
/// Why this exists: character text definitions live in <c>Templates</c> (<c>TemplateType.Character</c>), while the
/// Character Studio and the identity tables used to key on whichever scenario instance the studio was opened with.
/// The same character in two scenarios therefore had two identities — Dean had 8 packs on one instance and 1 on the
/// other, Becky's second scenario had none. Identity is owned by the template; this row records that an instance
/// belongs to one.
///
/// The row is NEVER inferred: not from a display name, not from a description, not from "the only template with a
/// similar name". A human makes the link, and <see cref="LinkedBy"/> is part of the record.
/// </summary>
public sealed class CharacterIdentityLink
{
    /// <summary>The scenario character id, or the <c>SceneAssets</c> character id, this link was made for.</summary>
    public string OwnerInstanceId { get; set; } = string.Empty;

    /// <summary>The <c>TemplateType.Character</c> template whose identity this instance resolves to.</summary>
    public string CharacterTemplateId { get; set; } = string.Empty;

    /// <summary>Who made the link (free text, for the audit trail).</summary>
    public string? LinkedBy { get; set; }

    public DateTime LinkedUtc { get; set; } = DateTime.UtcNow;
}
