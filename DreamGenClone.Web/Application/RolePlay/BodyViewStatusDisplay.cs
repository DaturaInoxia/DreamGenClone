using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// How a body view's status is SAID and COLOURED, in one place, because two surfaces now show the same fact: the
/// body panel's rows and the Body tab's left navigation.
///
/// <para>
/// The navigation exists so the operator can see, without opening anything, which cells are done and which still need
/// work. That only works if the nav and the panel cannot disagree about what a status means — two colour tables would
/// let the same view read green on the left and amber on the right, which is worse than showing nothing. The wording
/// is deliberately about the OPERATOR'S next move rather than the enum's name: "NotStarted" tells nobody anything,
/// "Not started" plus the colour says what to do.
/// </para>
/// </summary>
public static class BodyViewStatusDisplay
{
    /// <summary>The Bootstrap badge class for a status, or for a view that has no row yet.</summary>
    public static string BadgeClass(CharacterIdentityAngleStatus? status) => status switch
    {
        CharacterIdentityAngleStatus.Accepted => "text-bg-success",
        CharacterIdentityAngleStatus.Complete => "text-bg-info",
        CharacterIdentityAngleStatus.Pending => "text-bg-primary",
        CharacterIdentityAngleStatus.Failed or CharacterIdentityAngleStatus.Blocked => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    /// <summary>The badge class for a view row, where a missing row is its own state and is shown as not started.</summary>
    public static string BadgeClass(CharacterIdentityBodyView? view) => BadgeClass(view?.Status);

    /// <summary>What the operator reads in the surface's own words.</summary>
    public static string Label(CharacterIdentityAngleStatus? status) => status switch
    {
        CharacterIdentityAngleStatus.Accepted => "Accepted",
        CharacterIdentityAngleStatus.Complete => "Review",
        CharacterIdentityAngleStatus.Pending => "Rendering",
        CharacterIdentityAngleStatus.Failed => "Failed",
        CharacterIdentityAngleStatus.Blocked => "Blocked",
        _ => "Not started"
    };

    public static string Label(CharacterIdentityBodyView? view) => Label(view?.Status);

    /// <summary>
    /// Whether this cell still needs the operator. Accepted is the finish line; everything else — including a
    /// Complete image that has not been accepted — is work outstanding, which is the question the navigation answers.
    /// </summary>
    public static bool NeedsWork(CharacterIdentityBodyView? view)
        => view?.Status != CharacterIdentityAngleStatus.Accepted;

    /// <summary>Whether this cell is finished, for a group's "x of y" count.</summary>
    public static bool IsAccepted(CharacterIdentityBodyView? view)
        => view?.Status == CharacterIdentityAngleStatus.Accepted;

    /// <summary>
    /// Names a view as the thing a row is waiting for, e.g. "Unclothed 3/4 Left". Built from the ONE body-view
    /// vocabulary, so a prerequisite read here and the same view named elsewhere are the same words.
    /// </summary>
    public static string Prerequisite(CharacterIdentityBodyViewKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var state = IdentityPackReferenceLabels.BodyState(key.State);
        return key.View is { } view
            ? $"{state} {IdentityPackReferenceLabels.BodyView(view)}"
            : state;
    }
}
