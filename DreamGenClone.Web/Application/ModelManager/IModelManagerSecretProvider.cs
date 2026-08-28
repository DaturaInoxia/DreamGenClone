namespace DreamGenClone.Web.Application.ModelManager;

/// <summary>
/// Resolves per-instance API keys from the git-ignored <c>appsettings.Local.json</c>
/// (<c>ModelManagerSecrets</c> section). Lets operators keep instance keys out of git (and out of
/// the DB when preferred), feeding Model Manager at runtime — e.g. the RunPod API key needed by
/// serverless image endpoints.
/// </summary>
public interface IModelManagerSecretProvider
{
    /// <summary>Returns the plaintext secret for <paramref name="keyName"/>, or null if absent/empty.</summary>
    string? Resolve(string? keyName);
}
