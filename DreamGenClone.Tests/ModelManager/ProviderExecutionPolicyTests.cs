using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Tests.ModelManager;

public sealed class ProviderExecutionPolicyTests
{
    [Fact]
    public void Resolve_MapsOpenAiImagesToHostedApi()
    {
        var policy = ProviderExecutionPolicy.Resolve(new Provider { ImageProtocol = ImageProtocol.OpenAiImages });

        Assert.Equal(ProviderExecutionClass.HostedApi, policy.ExecutionClass);
    }

    [Fact]
    public void Resolve_MapsComfyUiToDedicatedPod()
    {
        var policy = ProviderExecutionPolicy.Resolve(new Provider { ImageProtocol = ImageProtocol.ComfyUi });

        Assert.Equal(ProviderExecutionClass.DedicatedPod, policy.ExecutionClass);
    }

    [Fact]
    public void Resolve_MapsComfyUiServerlessToSelfBuiltServerless()
    {
        var policy = ProviderExecutionPolicy.Resolve(new Provider
        {
            ImageProtocol = ImageProtocol.ComfyUiServerless,
            ReadinessPath = "/health"
        });

        Assert.Equal(ProviderExecutionClass.SelfBuiltServerless, policy.ExecutionClass);
    }

    [Fact]
    public void Resolve_ServerlessWithoutReadinessPathThrows()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ProviderExecutionPolicy.Resolve(new Provider
        {
            Name = "serverless-provider",
            ImageProtocol = ImageProtocol.ComfyUiServerless
        }));

        Assert.Equal(
            "Serverless provider 'serverless-provider' is missing ReadinessPath; serverless execution requires a readiness endpoint.",
            exception.Message);
    }

    [Fact]
    public void Resolve_ServerlessWithReadinessPathCarriesExecutionSettings()
    {
        var policy = ProviderExecutionPolicy.Resolve(new Provider
        {
            ImageProtocol = ImageProtocol.ComfyUiServerless,
            MaximumActiveRequests = 3,
            QueueCapacity = 7,
            ReadinessPath = "/health",
            ReadinessSuccessContractJson = "{\"status\":\"ready\"}"
        });

        Assert.Equal(3, policy.MaximumActiveRequests);
        Assert.Equal(7, policy.QueueCapacity);
        Assert.Equal("/health", policy.ReadinessPath);
        Assert.Equal("{\"status\":\"ready\"}", policy.ReadinessSuccessContractJson);
    }

    [Fact]
    public void Resolve_HostedApiWithoutReadinessPathSucceeds()
    {
        var policy = ProviderExecutionPolicy.Resolve(new Provider { ImageProtocol = ImageProtocol.OpenAiImages });

        Assert.Equal(ProviderExecutionClass.HostedApi, policy.ExecutionClass);
        Assert.Null(policy.ReadinessPath);
    }
}