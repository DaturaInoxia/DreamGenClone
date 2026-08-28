namespace DreamGenClone.Infrastructure.Configuration;

/// <summary>
/// Config section name for git-ignored per-instance secrets consumed by Model Manager. Lives in
/// <c>appsettings.Local.json</c> (never committed); the app reads it at runtime so instance API keys
/// never need to live in git or be typed into the DB.
/// <para>
/// Shape (each child key is a named plaintext secret, resolved by <c>IModelManagerSecretProvider</c>):
/// <code>{
///   "ModelManagerSecrets": {
///     "RunPod": "rps_...",
///     "Civitai": "..."
///   }
/// }</code>
/// </para>
/// </summary>
public sealed class ModelManagerSecretsOptions
{
    public const string SectionName = "ModelManagerSecrets";
}
