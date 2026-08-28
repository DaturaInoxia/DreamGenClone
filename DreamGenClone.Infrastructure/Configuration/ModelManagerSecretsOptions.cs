namespace DreamGenClone.Infrastructure.Configuration;

/// <summary>
/// Git-ignored per-instance secrets consumed by Model Manager. Lives in
/// <c>appsettings.Local.json</c> (never committed); the app reads it at runtime so instance API
/// keys never need to live in git or be typed into the DB.
/// </summary>
public sealed class ModelManagerSecretsOptions
{
    public const string SectionName = "ModelManagerSecrets";

    /// <summary>
    /// Named plaintext API keys, e.g. <c>{ "RunPod": "rps_...", "Civitai": "..." }</c>. Resolved by
    /// name via <c>IModelManagerSecretProvider</c>. A provider can reference a key by its
    /// <c>CredentialReference</c> (e.g. <c>runpod</c>) or the resolver falls back to the default
    /// <c>RunPod</c> entry for serverless providers.
    /// </summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
