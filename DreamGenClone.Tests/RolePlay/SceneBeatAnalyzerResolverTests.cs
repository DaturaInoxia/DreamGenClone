using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatAnalyzerResolverTests
{
    [Fact]
    public async Task Resolve_UsesOnlySceneBeatAnalyzerFunctionAndReturnsConfiguredSnapshot()
    {
        var fixture = CreateFixture();

        var resolved = await fixture.Resolver.ResolveAsync();

        Assert.Equal(AppFunction.RolePlaySceneBeatAnalyzer, fixture.FunctionDefaults.RequestedFunction);
        Assert.False(resolved.Model.IsSessionOverride);
        Assert.Equal("analyzer-model", resolved.ModelId);
        Assert.Equal("provider-1", resolved.ProviderId);
        Assert.Equal(3, resolved.MaxConcurrentJobs);
        Assert.Equal(120, resolved.LeaseSeconds);
        Assert.Equal(250, resolved.PollIntervalMilliseconds);
        Assert.Equal([5, 30], resolved.RetryDelaysSeconds);
        Assert.Equal(131072, resolved.MaximumContextTokens);
        Assert.Equal(8192, resolved.MaximumOutputTokens);
        Assert.Equal(8, resolved.MaximumCatalogueEntries);
    }

    [Fact]
    public async Task Resolve_MissingFunctionDefaultFailsExplicitly()
    {
        var fixture = CreateFixture();
        fixture.FunctionDefaults.Value = null;

        var exception = await Assert.ThrowsAsync<ModelResolutionException>(() => fixture.Resolver.ResolveAsync());

        Assert.Contains(AppFunction.RolePlaySceneBeatAnalyzer.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_ModelWithoutStructuredOutputModeFailsExplicitly()
    {
        var fixture = CreateFixture();
        fixture.Models.Value!.StructuredOutputMode = StructuredOutputMode.None;

        var exception = await Assert.ThrowsAsync<ModelResolutionException>(() => fixture.Resolver.ResolveAsync());

        Assert.Contains("structured-output mode", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_FunctionOutputAboveModelCapabilityFailsExplicitly()
    {
        var fixture = CreateFixture();
        fixture.Models.Value!.MaximumOutputTokens = 1000;

        var exception = await Assert.ThrowsAsync<ModelResolutionException>(() => fixture.Resolver.ResolveAsync());

        Assert.Contains("maximum output capability", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_UnspecifiedThinkingModeFailsExplicitly()
    {
        var fixture = CreateFixture();
        fixture.FunctionDefaults.Value!.ThinkingMode = ThinkingMode.Default;

        var exception = await Assert.ThrowsAsync<ModelResolutionException>(() => fixture.Resolver.ResolveAsync());

        Assert.Contains("explicit thinking mode", exception.Message, StringComparison.Ordinal);
    }

    private static TestFixture CreateFixture()
    {
        var functionDefaults = new FunctionDefaultRepositoryStub
        {
            Value = new FunctionModelDefault
            {
                Id = "function-default-1",
                FunctionName = AppFunction.RolePlaySceneBeatAnalyzer.ToString(),
                ModelId = "analyzer-model",
                Temperature = 0.2,
                TopP = 0.9,
                MaxTokens = 4000,
                ThinkingMode = ThinkingMode.Disabled,
                MaxConcurrentJobs = 3,
                DurableJobLeaseSeconds = 120,
                DurableJobPollIntervalMilliseconds = 250,
                TransientRetryCount = 2,
                TransientRetryDelaysSecondsJson = "[5,30]",
                DiagnosticsRetentionDays = 30,
                MaximumCatalogueEntries = 8
            }
        };
        var models = new RegisteredModelRepositoryStub
        {
            Value = new RegisteredModel
            {
                Id = "analyzer-model",
                ProviderId = "provider-1",
                ModelIdentifier = "model/structured",
                DisplayName = "Structured Analyzer",
                ModelKind = ModelKind.Text,
                IsEnabled = true,
                SupportsThinkingControl = true,
                StructuredOutputMode = StructuredOutputMode.StrictJsonSchema,
                MaximumContextTokens = 131072,
                MaximumOutputTokens = 8192
            }
        };
        var providers = new ProviderRepositoryStub
        {
            Value = new Provider
            {
                Id = "provider-1",
                Name = "Provider",
                BaseUrl = "https://provider.example",
                ChatCompletionsPath = "/v1/chat/completions",
                TimeoutSeconds = 120,
                IsEnabled = true
            }
        };
        return new TestFixture(new SceneBeatAnalyzerResolver(functionDefaults, models, providers), functionDefaults, models);
    }

    private sealed record TestFixture(
        SceneBeatAnalyzerResolver Resolver,
        FunctionDefaultRepositoryStub FunctionDefaults,
        RegisteredModelRepositoryStub Models);

    private sealed class FunctionDefaultRepositoryStub : IFunctionDefaultRepository
    {
        public FunctionModelDefault? Value { get; set; }
        public AppFunction? RequestedFunction { get; private set; }
        public Task<FunctionModelDefault?> GetByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default)
        {
            RequestedFunction = function;
            return Task.FromResult(Value);
        }
        public Task<FunctionModelDefault> SaveAsync(FunctionModelDefault functionDefault, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<FunctionModelDefault>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<FunctionModelDefault>> GetByModelIdAsync(string modelId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RegisteredModelRepositoryStub : IRegisteredModelRepository
    {
        public RegisteredModel? Value { get; set; }
        public Task<RegisteredModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task<RegisteredModel> SaveAsync(RegisteredModel model, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<RegisteredModel>> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<RegisteredModel>> GetAllEnabledAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByProviderAndIdentifierAsync(string providerId, string modelIdentifier, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ProviderRepositoryStub : IProviderRepository
    {
        public Provider? Value { get; set; }
        public Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task<Provider> SaveAsync(Provider provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Provider>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}