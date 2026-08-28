namespace DreamGenClone.Web.Domain.RolePlay;

/// <summary>
/// Lifecycle state of a background RP submission tracked by <see cref="DreamGenClone.Web.Application.RolePlay.IRolePlaySubmissionTracker"/>.
/// </summary>
public enum RolePlaySubmissionStatus
{
    /// <summary>Engine call is in progress; response not yet persisted.</summary>
    Running,

    /// <summary>Engine call succeeded and the response has been persisted to the session.</summary>
    Completed,

    /// <summary>Engine call threw an exception; entry is retained until the user acknowledges the failure.</summary>
    Failed
}
