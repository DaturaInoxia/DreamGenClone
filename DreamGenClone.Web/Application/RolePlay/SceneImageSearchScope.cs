namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// How far a search for the app's own rendered images reaches. It exists because "which images may I choose from?"
/// has a right answer that starts NARROW: while composing one moment, the images the operator made for it are what
/// they mean, and every other moment's rushes are a deliberate step outwards rather than the opening view.
/// </summary>
public enum SceneImageSearchScopeLevel
{
    /// <summary>This moment's own images, and nothing else. What a search opens on.</summary>
    Moment = 1,

    /// <summary>Every moment of this beat — the rushes either side of the one being composed.</summary>
    Beat = 2,

    /// <summary>Everything this session has rendered, whatever moment or beat produced it.</summary>
    Session = 3
}

/// <summary>
/// One search's scope: the session it is confined to, how far it reaches, and the ids that level needs.
///
/// <para>
/// The two ids are optional because the SESSION level needs neither — and they are REQUIRED by the levels that do,
/// checked rather than defaulted: a moment-scoped search with no moment id must fail with a diagnostic instead of
/// quietly widening to the whole session, which would answer a different question than the operator asked.
/// </para>
/// </summary>
public sealed record SceneImageSearchScope(
    string SessionId,
    SceneImageSearchScopeLevel Level,
    string? MomentId = null,
    string? BeatId = null);
