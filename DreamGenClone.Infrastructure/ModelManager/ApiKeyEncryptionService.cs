using System.Security.Cryptography;
using System.Text;
using DreamGenClone.Application.ModelManager;

namespace DreamGenClone.Infrastructure.ModelManager;

public sealed class ApiKeyEncryptionService : IApiKeyEncryptionService
{
    public string Encrypt(string plainTextApiKey)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainTextApiKey);
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    public string Decrypt(string encryptedApiKey)
    {
        var encryptedBytes = Convert.FromBase64String(encryptedApiKey);
        var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
