using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.ModelManager;

public sealed class ModelManagerSecretProvider : IModelManagerSecretProvider
{
    private readonly ModelManagerSecretsOptions _options;

    public ModelManagerSecretProvider(IOptions<ModelManagerSecretsOptions> options)
    {
        _options = options.Value;
    }

    public string? Resolve(string? keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return null;
        }
        return _options.Keys.TryGetValue(keyName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}
