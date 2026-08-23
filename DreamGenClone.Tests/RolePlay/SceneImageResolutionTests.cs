using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.ModelManager;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageResolutionTests
{
    private sealed class FakeFunctionDefaultRepository : IFunctionDefaultRepository
    {
        private readonly Dictionary<string, FunctionModelDefault> _byFunction = new(StringComparer.OrdinalIgnoreCase);
        public void Set(AppFunction function, FunctionModelDefault value) => _byFunction[function.ToString()] = value;

        public Task<FunctionModelDefault> SaveAsync(FunctionModelDefault functionDefault, CancellationToken cancellationToken = default)
        { _byFunction[functionDefault.FunctionName] = functionDefault; return Task.FromResult(functionDefault); }
        public Task<FunctionModelDefault?> GetByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default)
            => Task.FromResult(_byFunction.TryGetValue(function.ToString(), out var v) ? v : null);
        public Task<List<FunctionModelDefault>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_byFunction.Values.ToList());
        public Task<List<FunctionModelDefault>> GetByModelIdAsync(string modelId, CancellationToken cancellationToken = default)
            => Task.FromResult(_byFunction.Values.Where(x => x.ModelId == modelId).ToList());
        public Task<bool> DeleteByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default)
            => Task.FromResult(_byFunction.Remove(function.ToString()));
    }

    private sealed class FakeRegisteredModelRepository : IRegisteredModelRepository
    {
        private readonly Dictionary<string, RegisteredModel> _models = new(StringComparer.OrdinalIgnoreCase);
        public void Add(RegisteredModel model) => _models[model.Id] = model;

        public Task<RegisteredModel> SaveAsync(RegisteredModel model, CancellationToken cancellationToken = default)
        { _models[model.Id] = model; return Task.FromResult(model); }
        public Task<RegisteredModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_models.TryGetValue(id, out var m) ? m : null);
        public Task<List<RegisteredModel>> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default)
            => Task.FromResult(_models.Values.Where(x => x.ProviderId == providerId).ToList());
        public Task<List<RegisteredModel>> GetAllEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_models.Values.Where(x => x.IsEnabled).ToList());
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_models.Remove(id));
        public Task<bool> ExistsByProviderAndIdentifierAsync(string providerId, string modelIdentifier, CancellationToken cancellationToken = default)
            => Task.FromResult(_models.Values.Any(x => x.ProviderId == providerId && x.ModelIdentifier == modelIdentifier));
    }

    private sealed class FakeProviderRepository : IProviderRepository
    {
        private readonly Dictionary<string, Provider> _providers = new(StringComparer.OrdinalIgnoreCase);
        public void Add(Provider provider) => _providers[provider.Id] = provider;

        public Task<Provider> SaveAsync(Provider provider, CancellationToken cancellationToken = default)
        { _providers[provider.Id] = provider; return Task.FromResult(provider); }
        public Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_providers.TryGetValue(id, out var p) ? p : null);
        public Task<List<Provider>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_providers.Values.ToList());
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_providers.Remove(id));
        public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(_providers.Values.Any(x => x.Name == name));
    }

    private static (ModelResolutionService service, FakeFunctionDefaultRepository funcDefaults, FakeRegisteredModelRepository models, FakeProviderRepository providers) Build()
    {
        var funcDefaults = new FakeFunctionDefaultRepository();
        var models = new FakeRegisteredModelRepository();
        var providers = new FakeProviderRepository();
        var service = new ModelResolutionService(funcDefaults, models, providers, NullLogger<ModelResolutionService>.Instance);
        return (service, funcDefaults, models, providers);
    }

    private static void SeedHappyPath(FakeFunctionDefaultRepository funcDefaults, FakeRegisteredModelRepository models, FakeProviderRepository providers)
    {
        var provider = new Provider
        {
            Id = "prov-1",
            Name = "Together",
            ProviderType = ProviderType.TogetherAI,
            BaseUrl = "https://api.together.ai",
            ImageCapability = ImageProviderCapability.TextAndImage,
            ImageGenerationPath = "/v1/images/generations",
            ContentPolicy = ImageContentPolicy.AdultAllowed,
            IsEnabled = true
        };
        providers.Add(provider);

        var model = new RegisteredModel
        {
            Id = "model-1",
            ProviderId = "prov-1",
            ModelIdentifier = "black-forest-labs/FLUX.1-schnell",
            DisplayName = "FLUX",
            ModelKind = ModelKind.Image,
            IsEnabled = true
        };
        models.Add(model);

        funcDefaults.Set(AppFunction.RolePlaySceneImage, new FunctionModelDefault
        {
            FunctionName = AppFunction.RolePlaySceneImage.ToString(),
            ModelId = "model-1",
            Temperature = 0.7,
            TopP = 0.9,
            MaxTokens = 500
        });
    }

    [Fact]
    public async Task ResolveImageModel_NoFunctionDefault_FailsFast()
    {
        var (service, _, _, _) = Build();
        var ex = await Assert.ThrowsAsync<ModelResolutionException>(
            () => service.ResolveImageModelAsync(null, CancellationToken.None));
        Assert.Contains("No image model configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveImageModel_TextOnlyModel_FailsFast()
    {
        var (service, funcDefaults, models, providers) = Build();
        SeedHappyPath(funcDefaults, models, providers);
        models.Add(new RegisteredModel
        {
            Id = "model-1",
            ProviderId = "prov-1",
            ModelIdentifier = "deepseek",
            DisplayName = "DeepSeek",
            ModelKind = ModelKind.Text,   // <-- not an image model
            IsEnabled = true
        });

        var ex = await Assert.ThrowsAsync<ModelResolutionException>(
            () => service.ResolveImageModelAsync(null, CancellationToken.None));
        Assert.Contains("not an image model", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveImageModel_ProviderNotImageCapable_FailsFast()
    {
        var (service, funcDefaults, models, providers) = Build();
        SeedHappyPath(funcDefaults, models, providers);
        providers.Add(new Provider
        {
            Id = "prov-1",
            Name = "Together",
            ImageCapability = ImageProviderCapability.None, // <-- not image-capable
            ContentPolicy = ImageContentPolicy.AdultAllowed,
            IsEnabled = true
        });

        var ex = await Assert.ThrowsAsync<ModelResolutionException>(
            () => service.ResolveImageModelAsync(null, CancellationToken.None));
        Assert.Contains("not image-capable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveImageModel_UnknownContentPolicy_FailsFast()
    {
        var (service, funcDefaults, models, providers) = Build();
        SeedHappyPath(funcDefaults, models, providers);
        providers.Add(new Provider
        {
            Id = "prov-1",
            Name = "Together",
            ImageCapability = ImageProviderCapability.TextAndImage,
            ContentPolicy = ImageContentPolicy.Unknown, // <-- unset policy
            IsEnabled = true
        });

        var ex = await Assert.ThrowsAsync<ModelResolutionException>(
            () => service.ResolveImageModelAsync(null, CancellationToken.None));
        Assert.Contains("content policy", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveImageModel_HappyPath_ReturnsResolvedImageModel()
    {
        var (service, funcDefaults, models, providers) = Build();
        SeedHappyPath(funcDefaults, models, providers);

        var resolved = await service.ResolveImageModelAsync(null, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal("https://api.together.ai", resolved.ProviderBaseUrl);
        Assert.Equal("/v1/images/generations", resolved.ImageGenerationPath);
        Assert.Equal("black-forest-labs/FLUX.1-schnell", resolved.ModelIdentifier);
        Assert.Equal(ImageContentPolicy.AdultAllowed, resolved.ContentPolicy);
        Assert.Equal("Together", resolved.ProviderName);
        Assert.False(resolved.IsSessionOverride);
    }

    [Fact]
    public async Task ResolveImageModel_ComfyUiProvider_SetsProtocolAndUrl()
    {
        var (service, funcDefaults, models, providers) = Build();
        var provider = new Provider
        {
            Id = "prov-comfy",
            Name = "RunPod ComfyUI",
            ProviderType = ProviderType.TogetherAI,
            BaseUrl = "https://qguv5e029u58lb-3000.proxy.runpod.net",
            ImageCapability = ImageProviderCapability.ImageOnly,
            ImageGenerationPath = "/prompt",
            ContentPolicy = ImageContentPolicy.AdultAllowed,
            ImageProtocol = ImageProtocol.ComfyUi,
            IsEnabled = true
        };
        providers.Add(provider);
        models.Add(new RegisteredModel
        {
            Id = "model-comfy",
            ProviderId = "prov-comfy",
            ModelIdentifier = "ponyDiffusionV6XL_v6.safetensors",
            DisplayName = "PonyV6",
            ModelKind = ModelKind.Image,
            IsEnabled = true
        });
        funcDefaults.Set(AppFunction.RolePlaySceneImage, new FunctionModelDefault
        {
            FunctionName = AppFunction.RolePlaySceneImage.ToString(),
            ModelId = "model-comfy",
            Temperature = 0.7,
            TopP = 0.9,
            MaxTokens = 500
        });

        var resolved = await service.ResolveImageModelAsync(null, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(ImageProtocol.ComfyUi, resolved.ImageProtocol);
        Assert.Equal("https://qguv5e029u58lb-3000.proxy.runpod.net", resolved.ComfyUiUrl);
        Assert.Equal("ponyDiffusionV6XL_v6.safetensors", resolved.ModelIdentifier);
    }

    [Fact]
    public async Task ResolveImagePromptModel_NoPreprocessorDefault_FailsFast()
    {
        var (service, _, _, _) = Build();
        var ex = await Assert.ThrowsAsync<ModelResolutionException>(
            () => service.ResolveImagePromptModelAsync(null, CancellationToken.None));
        Assert.Contains("No model configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveImagePromptModel_HappyPath_ReturnsTextModel()
    {
        var (service, funcDefaults, models, providers) = Build();
        var provider = new Provider { Id = "prov-1", Name = "DeepSeekHost", ProviderType = ProviderType.TogetherAI, BaseUrl = "https://api.deepseek", IsEnabled = true };
        providers.Add(provider);
        models.Add(new RegisteredModel { Id = "model-1", ProviderId = "prov-1", ModelIdentifier = "deepseek-chat", DisplayName = "DeepSeek", ModelKind = ModelKind.Text, IsEnabled = true });
        funcDefaults.Set(AppFunction.RolePlaySceneImagePreprocessor, new FunctionModelDefault
        {
            FunctionName = AppFunction.RolePlaySceneImagePreprocessor.ToString(),
            ModelId = "model-1",
            Temperature = 0.7,
            TopP = 0.9,
            MaxTokens = 1000
        });

        var resolved = await service.ResolveImagePromptModelAsync(null, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal("deepseek-chat", resolved.ModelIdentifier);
        Assert.Equal(1000, resolved.MaxTokens);
    }
}
