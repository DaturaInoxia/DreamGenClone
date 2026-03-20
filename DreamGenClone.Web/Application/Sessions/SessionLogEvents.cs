using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Sessions;

public static class SessionLogEvents
{
    public static readonly EventId RetrievedSessions = new(3001, nameof(RetrievedSessions));
    public static readonly EventId PersistedSession = new(3002, nameof(PersistedSession));
    public static readonly EventId DeletedSession = new(3003, nameof(DeletedSession));
    public static readonly EventId OpenRolePlaySession = new(3004, nameof(OpenRolePlaySession));
    public static readonly EventId DeleteRolePlaySession = new(3005, nameof(DeleteRolePlaySession));
}
