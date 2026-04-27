using System.Security.Cryptography;
using System.Text;
using DreamGenClone.Application.ModelManager;

namespace DreamGenClone.Infrastructure.ModelManager;

public sealed class ApiKeyEncryptionService : IApiKeyEncryptionService
{
    public string Encrypt(string plainTextApiKey)
    {
        if (OperatingSystem.IsWindows())
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainTextApiKey);
            var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }

        throw new PlatformNotSupportedException("API key encryption is only supported on Windows.");
    }

    public string Decrypt(string encryptedApiKey)
    {
        if (OperatingSystem.IsWindows())
        {
            var encryptedBytes = Convert.FromBase64String(encryptedApiKey);
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }

        throw new PlatformNotSupportedException("API key decryption is only supported on Windows.");
    }
}
