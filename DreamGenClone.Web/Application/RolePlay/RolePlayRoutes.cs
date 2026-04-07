namespace DreamGenClone.Web.Application.RolePlay;

public static class RolePlayRoutes
{
    public const string LegacyRolePlay = "/roleplay";
    public const string Create = "/roleplay/create";
    public const string Sessions = "/roleplay/sessions";
    public const string Workspace = "/roleplay/workspace/{sessionId}";
    public const string Debug = "/roleplay/debug/{sessionId}";

    public static string GetWorkspace(string sessionId) => $"/roleplay/workspace/{sessionId}";

    public static string GetDebug(string sessionId) => $"/roleplay/debug/{sessionId}";
}
