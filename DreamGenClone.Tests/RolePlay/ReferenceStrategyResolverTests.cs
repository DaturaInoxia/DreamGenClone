using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ReferenceStrategyResolverTests
{
    [Fact]
    public void HostedApi_AllowsTextOnly_ButRejectsGraphStrategy()
    {
        var model = CreateModel();
        var provider = CreateProvider(ImageProtocol.OpenAiImages);

        var text = ReferenceStrategyResolver.Resolve(model, provider, "TextOnly");
        var graph = ReferenceStrategyResolver.Resolve(model, provider, "ControlNet");

        Assert.Equal(ReferenceStrategyResolutionStatus.Possible, text.Status);
        Assert.Equal(ReferenceStrategyResolutionStatus.Impossible, graph.Status);
    }

    [Fact]
    public void GraphStrategy_RequiresModelDeclaration()
    {
        var model = CreateModel();
        var provider = CreateProvider(ImageProtocol.ComfyUi);

        var result = ReferenceStrategyResolver.Resolve(model, provider, "ControlNet");

        Assert.Equal(ReferenceStrategyResolutionStatus.Unqualified, result.Status);
    }

    [Fact]
    public void GraphStrategy_RequiresQualificationForTheExactEndpoint()
    {
        var provider = CreateProvider(ImageProtocol.ComfyUi);
        var model = CreateModel("[\"ControlNet\"]", "[{\"strategy\":\"ControlNet\",\"endpointId\":\"other-endpoint\",\"qualified\":true,\"proofId\":\"proof-1\"}]");

        var result = ReferenceStrategyResolver.Resolve(model, provider, "ControlNet");

        Assert.Equal(ReferenceStrategyResolutionStatus.Unqualified, result.Status);
    }

    [Fact]
    public void GraphStrategy_IsPossibleWhenDeclaredAndQualified()
    {
        var provider = CreateProvider(ImageProtocol.ComfyUiServerless);
        var model = CreateModel(
            "[\"ControlNet\"]",
            $"[{{\"strategy\":\"ControlNet\",\"endpointId\":\"{provider.Id}\",\"qualified\":true,\"proofId\":\"proof-1\"}}]");

        var result = ReferenceStrategyResolver.Resolve(model, provider, "ControlNet");

        Assert.Equal(ReferenceStrategyResolutionStatus.Possible, result.Status);
        Assert.True(result.IsAvailable);
    }

    private static RegisteredModel CreateModel(
        string visualStrategies = "[]",
        string qualifications = "[]") => new()
    {
        DisplayName = "Test image model",
        SupportedVisualStrategiesJson = visualStrategies,
        CapabilityQualificationsJson = qualifications
    };

    private static Provider CreateProvider(ImageProtocol protocol) => new()
    {
        ImageProtocol = protocol
    };
}

internal sealed class TestReferenceStrategyResolver : IReferenceStrategyResolver
{
    public Task<ReferenceStrategyResolution> ResolveAsync(
        string modelId,
        string strategy,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReferenceStrategyResolution(
            ReferenceStrategyResolutionStatus.Possible,
            strategy,
            "Test strategy is explicitly available."));
}