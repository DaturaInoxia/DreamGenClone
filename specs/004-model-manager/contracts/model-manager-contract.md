# Contracts: Model Manager

**Feature**: 004-model-manager | **Date**: 2026-04-03

This document defines the service boundary interfaces introduced or modified by the Model Manager feature. These are the public contracts consumed by other layers.

---

## ICompletionClient (replaces ILmStudioClient)

**Layer**: DreamGenClone.Application/Abstractions  
**Consumers**: All services that make generation calls (6 existing + ModelProcessingWorker)

```csharp
namespace DreamGenClone.Application.Abstractions;

public interface ICompletionClient
{
    /// <summary>
    /// Generates a completion using a resolved model configuration.
    /// Provider URL, auth, model, and parameters are all derived from the ResolvedModel.
    /// </summary>
    Task<string> GenerateAsync(
        string prompt,
        ResolvedModel resolved,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a completion with separate system and user messages.
    /// </summary>
    Task<string> GenerateAsync(
        string systemMessage,
        string userMessage,
        ResolvedModel resolved,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connectivity to a provider endpoint.
    /// Used by the "Test Connection" feature on the Model Manager page.
    /// </summary>
    Task<bool> CheckHealthAsync(
        string providerBaseUrl,
        int timeoutSeconds,
        string? decryptedApiKey,
        CancellationToken cancellationToken = default);
}
```

**Notes**:
- `ResolvedModel` is a value object containing all provider + model + parameter info (see data-model.md)
- `CheckHealthAsync` takes explicit parameters (not `ResolvedModel`) because health checks happen before a model is resolved
- The decrypted API key is passed to `CheckHealthAsync` — the caller (facade) handles decryption

---

## IModelResolutionService

**Layer**: DreamGenClone.Application/ModelManager  
**Consumers**: All 6 generation services + ModelProcessingWorker

```csharp
namespace DreamGenClone.Application.ModelManager;

public interface IModelResolutionService
{
    /// <summary>
    /// Resolves the model configuration for a given application function.
    /// Fallback chain: sessionOverride → function default → error.
    /// </summary>
    /// <param name="function">The application function requiring a model.</param>
    /// <param name="sessionModelId">Optional per-session model override ID. If set, resolves this model instead of the function default.</param>
    /// <param name="sessionTemperature">Temperature override when session model is set.</param>
    /// <param name="sessionTopP">TopP override when session model is set.</param>
    /// <param name="sessionMaxTokens">MaxTokens override when session model is set.</param>
    /// <returns>A ResolvedModel with all necessary call parameters, or throws if resolution fails.</returns>
    Task<ResolvedModel> ResolveAsync(
        AppFunction function,
        string? sessionModelId = null,
        double? sessionTemperature = null,
        double? sessionTopP = null,
        int? sessionMaxTokens = null,
        CancellationToken cancellationToken = default);
}
```

**Error Contract**:
- Throws `ModelResolutionException` when no model is configured for the function (no session override and no function default)
- Throws `ModelResolutionException` when the resolved model or its provider is disabled
- All exceptions include an actionable message directing the user to the Model Manager page

---

## IProviderRepository

**Layer**: DreamGenClone.Application/ModelManager  
**Implementation**: DreamGenClone.Infrastructure/ModelManager/ProviderRepository.cs

```csharp
namespace DreamGenClone.Application.ModelManager;

public interface IProviderRepository
{
    Task<Provider> SaveAsync(Provider provider, CancellationToken cancellationToken = default);
    Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<List<Provider>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
}
```

---

## IRegisteredModelRepository

**Layer**: DreamGenClone.Application/ModelManager  
**Implementation**: DreamGenClone.Infrastructure/ModelManager/RegisteredModelRepository.cs

```csharp
namespace DreamGenClone.Application.ModelManager;

public interface IRegisteredModelRepository
{
    Task<RegisteredModel> SaveAsync(RegisteredModel model, CancellationToken cancellationToken = default);
    Task<RegisteredModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<List<RegisteredModel>> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default);
    Task<List<RegisteredModel>> GetAllEnabledAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByProviderAndIdentifierAsync(string providerId, string modelIdentifier, CancellationToken cancellationToken = default);
}
```

---

## IFunctionDefaultRepository

**Layer**: DreamGenClone.Application/ModelManager  
**Implementation**: DreamGenClone.Infrastructure/ModelManager/FunctionDefaultRepository.cs

```csharp
namespace DreamGenClone.Application.ModelManager;

public interface IFunctionDefaultRepository
{
    Task<FunctionModelDefault> SaveAsync(FunctionModelDefault functionDefault, CancellationToken cancellationToken = default);
    Task<FunctionModelDefault?> GetByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default);
    Task<List<FunctionModelDefault>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<FunctionModelDefault>> GetByModelIdAsync(string modelId, CancellationToken cancellationToken = default);
    Task<bool> DeleteByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default);
}
```

---

## IApiKeyEncryptionService

**Layer**: DreamGenClone.Application/ModelManager  
**Implementation**: DreamGenClone.Infrastructure/ModelManager/ApiKeyEncryptionService.cs

```csharp
namespace DreamGenClone.Application.ModelManager;

public interface IApiKeyEncryptionService
{
    /// <summary>
    /// Encrypts a plaintext API key using DPAPI and returns a Base64-encoded ciphertext string.
    /// </summary>
    string Encrypt(string plainTextApiKey);

    /// <summary>
    /// Decrypts a Base64-encoded DPAPI ciphertext string and returns the plaintext API key.
    /// Throws CryptographicException if decryption fails (e.g., DPAPI scope changed).
    /// </summary>
    string Decrypt(string encryptedApiKey);
}
```

**Error Contract**:
- `Decrypt` throws `CryptographicException` when the encrypted data cannot be decrypted (DPAPI scope change, data corruption)
- Callers must handle this and surface an actionable message to re-enter the API key

---

## Service Refactoring Contract

Each of the 6 existing generation services transitions from this pattern:

```csharp
// Before
public class SomeService
{
    public SomeService(
        ILmStudioClient lmClient,
        IOptions<SomeOptions> options,
        IOptions<LmStudioOptions> lmOptions) { }
}
```

To this pattern:

```csharp
// After
public class SomeService
{
    public SomeService(
        ICompletionClient completionClient,
        IModelResolutionService modelResolver) { }
}
```

**Affected Services**:

| Service | AppFunction(s) Used | Location |
|---------|-------------------|----------|
| RolePlayContinuationService | RolePlayGeneration | Web/Application/RolePlay/ |
| StoryEngineService | StoryModeGeneration | Web/Application/Story/ |
| StorySummaryService | StorySummarize | Infrastructure/StoryAnalysis/ |
| StoryAnalysisService | StoryAnalyze | Infrastructure/StoryAnalysis/ |
| StoryRankingService | StoryRank | Infrastructure/StoryAnalysis/ |
| ScenarioAdaptationService | ScenarioPreview, ScenarioAdapt | Web/Application/Scenarios/ |
| WritingAssistantService | WritingAssistant | Web/Application/Assistants/ |
| RolePlayAssistantService | RolePlayAssistant | Web/Application/Assistants/ |

*Note*: ScenarioAdaptationService uses two different AppFunctions (ScenarioPreview and ScenarioAdapt) with different parameters. Each call resolves independently.

---

## DI Registration Contract

New registrations in `Program.cs`:

```csharp
// Model Manager repositories (Singleton — stateless, SQLite is thread-safe for reads)
builder.Services.AddSingleton<IProviderRepository, ProviderRepository>();
builder.Services.AddSingleton<IRegisteredModelRepository, RegisteredModelRepository>();
builder.Services.AddSingleton<IFunctionDefaultRepository, FunctionDefaultRepository>();
builder.Services.AddSingleton<IApiKeyEncryptionService, ApiKeyEncryptionService>();

// Model resolution (Scoped — may hold session context)
builder.Services.AddScoped<IModelResolutionService, ModelResolutionService>();
builder.Services.AddScoped<ModelManagerFacade>();
builder.Services.AddScoped<ProviderTestService>();

// Replace ILmStudioClient registration with ICompletionClient
// Before: builder.Services.AddHttpClient<ILmStudioClient, LmStudioClient>(...)
// After:
builder.Services.AddSingleton<ICompletionClient, CompletionClient>();
// CompletionClient receives IHttpClientFactory internally
builder.Services.AddHttpClient("CompletionClient");
```
