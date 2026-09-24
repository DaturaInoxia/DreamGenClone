namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>What an identity owner id turned out to be.</summary>
public enum CharacterIdentityOwnerKind
{
    /// <summary>The character template itself — the canonical identity owner (B-127).</summary>
    CharacterTemplate = 1,

    /// <summary>A scenario character, which resolves to its character template.</summary>
    ScenarioCharacter = 2,

    /// <summary>A <c>SceneAssets</c> character, which resolves through an explicit link to a template.</summary>
    AssetCharacter = 3
}

/// <summary>
/// The character a piece of identity belongs to (B-127), resolved from whatever id the caller arrived with.
///
/// <see cref="TemplateId"/> is the identity KEY: packs, builds, body cards and LoRA datasets belong to it. The
/// instance fields say where the caller came from, so the UI can be honest about it ("Becky — Campground Intimacy")
/// instead of pretending two scenario instances are one character.
/// </summary>
public sealed record CharacterIdentityOwner(
    CharacterIdentityOwnerKind Kind,
    string TemplateId,
    string TemplateName,
    string InstanceId,
    string InstanceName)
{
    /// <summary>The owner namespace, in the words the UI uses.</summary>
    public string DescribeKind() => CharacterIdentityOwnerKinds.Describe(Kind);

    /// <summary>True when the caller arrived at the character rather than at its template.</summary>
    public bool IsInstance => Kind != CharacterIdentityOwnerKind.CharacterTemplate;
}

/// <summary>
/// A character that resolves to NO template (B-127) — the work list of the explicit link action. <see cref="Reason"/> is
/// the resolver's own refusal text, so the UI repeats why the character is unlinked instead of paraphrasing it.
/// </summary>
public sealed record CharacterIdentityCandidate(
    string InstanceId,
    string InstanceName,
    CharacterIdentityOwnerKind Kind,
    string Reason,
    bool CanLink)
{
    /// <summary>The owner namespace, in the words the UI uses.</summary>
    public string DescribeKind() => CharacterIdentityOwnerKinds.Describe(Kind);
}

/// <summary>One place that turns an owner kind into the words the UI shows, so the labels cannot drift apart.</summary>
public static class CharacterIdentityOwnerKinds
{
    public static string Describe(CharacterIdentityOwnerKind kind) => kind switch
    {
        CharacterIdentityOwnerKind.CharacterTemplate => "character template",
        CharacterIdentityOwnerKind.ScenarioCharacter => "scenario character",
        CharacterIdentityOwnerKind.AssetCharacter => "asset character",
        _ => throw new InvalidOperationException($"Unsupported character identity owner kind '{kind}'.")
    };
}
