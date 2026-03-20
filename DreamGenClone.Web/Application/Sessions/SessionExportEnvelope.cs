using System.Text.Json;

namespace DreamGenClone.Web.Application.Sessions;

public sealed class SessionExportEnvelope
{
    public int SchemaVersion { get; set; } = 1;

    public string SessionType { get; set; } = string.Empty;

    public JsonElement Payload { get; set; }
}
