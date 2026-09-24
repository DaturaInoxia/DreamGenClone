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

/// <summary>
/// HOW a model carries IDENTITY, decided in one place so the picker's answer and the render's answer cannot differ. A
/// dedicated mechanism wins; a native-reference model (Qwen-Image-2.1, which declares no mechanism at all) is next;
/// and when neither is available the reason offered is the ACTIONABLE one, because "declare this in Model Manager" is
/// something the operator can act on and "this endpoint can never do it" is not.
/// </summary>
public sealed class IdentityStrategyDecisionTests
{
    [Fact]
    public async Task PrefersTheConfiguredMechanism_WhenBothAreAvailable()
    {
        var resolver = new MapReferenceStrategies
        {
            Statuses =
            {
                ["ReferenceConditioning"] = ReferenceStrategyResolutionStatus.Possible,
                ["NativeMultiReference"] = ReferenceStrategyResolutionStatus.Possible
            }
        };

        var result = await ReferenceStrategyResolver.ResolveIdentityAsync(resolver, "model-1");

        // The dedicated mechanism is preferred whenever it resolves, even though the native path would have answered
        // too â€” a configured IP-Adapter/PuLID graph is a stronger identity signal than a reference image slot.
        Assert.True(result.IsAvailable);
        Assert.Equal(ReferenceStrategyResolver.IdentityReferenceConditioning, result.Strategy);
    }

    [Fact]
    public async Task FallsBackToNativeReferences_WhenNoMechanismIsQualified()
    {
        var resolver = new MapReferenceStrategies
        {
            Statuses =
            {
                ["ReferenceConditioning"] = ReferenceStrategyResolutionStatus.Unqualified,
                ["NativeMultiReference"] = ReferenceStrategyResolutionStatus.Possible
            }
        };

        var result = await ReferenceStrategyResolver.ResolveIdentityAsync(resolver, "model-1");

        Assert.True(result.IsAvailable);
        Assert.Equal(ReferenceStrategyResolver.IdentityNativeMultiReference, result.Strategy);
    }

    [Fact]
    public async Task PrefersTheActionableReason_WhenTheMechanismIsUnqualifiedAndTheNativePathIsImpossible()
    {
        var resolver = new MapReferenceStrategies
        {
            Statuses =
            {
                ["ReferenceConditioning"] = ReferenceStrategyResolutionStatus.Unqualified,
                ["NativeMultiReference"] = ReferenceStrategyResolutionStatus.Impossible
            }
        };

        var result = await ReferenceStrategyResolver.ResolveIdentityAsync(resolver, "model-1");

        Assert.False(result.IsAvailable);
        Assert.Equal(ReferenceStrategyResolver.IdentityReferenceConditioning, result.Strategy);
        Assert.Equal(ReferenceStrategyResolutionStatus.Unqualified, result.Status);
    }

    [Fact]
    public async Task ReportsThePrimaryMechanism_WhenBothAreUnavailableTheSameWay()
    {
        var resolver = new MapReferenceStrategies
        {
            Statuses =
            {
                ["ReferenceConditioning"] = ReferenceStrategyResolutionStatus.Unqualified,
                ["NativeMultiReference"] = ReferenceStrategyResolutionStatus.Unqualified
            }
        };

        var result = await ReferenceStrategyResolver.ResolveIdentityAsync(resolver, "model-1");

        // Same answer every time, rather than one that depends on which mechanism is declared less badly.
        Assert.False(result.IsAvailable);
        Assert.Equal(ReferenceStrategyResolver.IdentityReferenceConditioning, result.Strategy);
    }

    /// <summary>
    /// A resolver whose answers are declared per strategy, so every branch of the decision is reachable â€” including
    /// the branches a real model row would need a ProofId to reach. Shared by the identity and pose decision tests.
    /// </summary>
    internal sealed class MapReferenceStrategies : IReferenceStrategyResolver
    {
        /// <summary>Declared per strategy; anything unlisted is Unqualified, as an undeclared model row would be.</summary>
        internal Dictionary<string, ReferenceStrategyResolutionStatus> Statuses { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Task<ReferenceStrategyResolution> ResolveAsync(
            string modelId, string strategy, CancellationToken cancellationToken = default)
        {
            var status = Statuses.TryGetValue(strategy, out var declared)
                ? declared
                : ReferenceStrategyResolutionStatus.Unqualified;

            return Task.FromResult(new ReferenceStrategyResolution(
                status, strategy, $"'{strategy}' is {status} for model '{modelId}'."));
        }
    }
}

/// <summary>
/// HOW a model carries a POSE, decided in the same one place as identity and through the same contract. A qualified
/// OpenPose ControlNet graph wins; a native-reference model is next, because a skeleton placed in a reference slot is
/// read as pose guidance (measured on the host 2026-09-23); and when neither is available the reason is the
/// actionable one.
/// </summary>
public sealed class PoseStrategyDecisionTests
{
    [Fact]
    public async Task PrefersTheControlNetGraph_WhenBothAreAvailable()
    {
        var resolver = new IdentityStrategyDecisionTests.MapReferenceStrategies
        {
            Statuses =
            {
                [ReferenceStrategyResolver.PoseControlNet] = ReferenceStrategyResolutionStatus.Possible,
                [ReferenceStrategyResolver.IdentityNativeMultiReference] = ReferenceStrategyResolutionStatus.Possible
            }
        };

        var result = await ReferenceStrategyResolver.ResolvePoseAsync(resolver, "model-1");

        Assert.True(result.IsAvailable);
        Assert.Equal(ReferenceStrategyResolver.PoseControlNet, result.Strategy);
    }

    [Fact]
    public async Task UsesNativeReferences_WhenTheModelHasNoControlNet()
    {
        var resolver = new IdentityStrategyDecisionTests.MapReferenceStrategies
        {
            Statuses =
            {
                [ReferenceStrategyResolver.PoseControlNet] = ReferenceStrategyResolutionStatus.Unqualified,
                [ReferenceStrategyResolver.IdentityNativeMultiReference] = ReferenceStrategyResolutionStatus.Possible
            }
        };

        var result = await ReferenceStrategyResolver.ResolvePoseAsync(resolver, "model-1");

        // This is the Qwen-Image-2.1 case: no ControlNet weights exist for the architecture, so the skeleton travels
        // as one more reference image instead.
        Assert.True(result.IsAvailable);
        Assert.Equal(ReferenceStrategyResolver.IdentityNativeMultiReference, result.Strategy);
    }

    [Fact]
    public async Task FailsWithTheActionableReason_WhenNeitherRouteIsQualified()
    {
        var resolver = new IdentityStrategyDecisionTests.MapReferenceStrategies
        {
            Statuses =
            {
                [ReferenceStrategyResolver.PoseControlNet] = ReferenceStrategyResolutionStatus.Unqualified,
                [ReferenceStrategyResolver.IdentityNativeMultiReference] = ReferenceStrategyResolutionStatus.Impossible
            }
        };

        var result = await ReferenceStrategyResolver.ResolvePoseAsync(resolver, "model-1");

        Assert.False(result.IsAvailable);
        Assert.Equal(ReferenceStrategyResolver.PoseControlNet, result.Strategy);
        Assert.Equal(ReferenceStrategyResolutionStatus.Unqualified, result.Status);
    }
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