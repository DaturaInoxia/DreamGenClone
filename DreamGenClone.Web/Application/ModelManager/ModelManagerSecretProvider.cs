using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace DreamGenClone.Web.Application.ModelManager;

public sealed class ModelManagerSecretProvider : IModelManagerSecretProvider
{
    private readonly Dictionary<string, string> _keys;

    public ModelManagerSecretProvider(IConfiguration configuration)
    {
        // Bind the ModelManagerSecrets section's children directly as named keys, e.g.
        //   "ModelManagerSecrets": { "RunPod": "rps_...", "Civitai": "..." }
        // (each child key => plaintext secret). Bound via IConfiguration so the git-ignored
        // appsettings.Local.json shape works without a nested "Keys" property.
        _keys = configuration.GetSection(ModelManagerSecretsOptions.SectionName)
                             .Get<Dictionary<string, string>>()
                 ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public string? Resolve(string? keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return null;
        }
        return _keys.TryGetValue(keyName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}
