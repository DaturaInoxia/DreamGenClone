namespace DreamGenClone.Application.ModelManager;

public interface IApiKeyEncryptionService
{
    string Encrypt(string plainTextApiKey);
    string Decrypt(string encryptedApiKey);
}
