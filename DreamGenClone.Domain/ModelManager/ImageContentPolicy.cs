namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// Adult-content capability of an image provider. Resolved at generation time from the provider
/// record — never assumed. The capability is retained for provider refusal and edit enforcement.
/// </summary>
public enum ImageContentPolicy
{
    /// <summary>Not configured — image resolution fails fast with guidance (no silent SFW assumption).</summary>
    Unknown = 0,

    /// <summary>Provider filters/blocks adult content (e.g. default cloud tier).</summary>
    SfwFiltered = 1,

    /// <summary>Provider allows adult content (e.g. adult-approved account).</summary>
    AdultAllowed = 2,

    /// <summary>Provider permits adult via account/flag — surfaced in UI for confirmation.</summary>
    AdultAllowedConfigurable = 3
}
