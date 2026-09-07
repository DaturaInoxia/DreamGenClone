namespace DreamGenClone.Domain.ModelManager;

public sealed record ProviderExecutionPolicy(
    ProviderExecutionClass ExecutionClass,
    int TimeoutSeconds,
    int? MaximumActiveRequests,
    int? QueueCapacity,
    string? ReadinessPath,
    string? ReadinessSuccessContractJson)
{
    public static ProviderExecutionPolicy Resolve(Provider provider)
    {
        var executionClass = provider.ImageProtocol switch
        {
            ImageProtocol.OpenAiImages => ProviderExecutionClass.HostedApi,
            ImageProtocol.ComfyUi => ProviderExecutionClass.DedicatedPod,
            ImageProtocol.ComfyUiServerless => ProviderExecutionClass.SelfBuiltServerless,
            _ => throw new InvalidOperationException(
                $"Unknown ImageProtocol '{provider.ImageProtocol}' — cannot resolve execution class.")
        };

        if (executionClass == ProviderExecutionClass.SelfBuiltServerless
            && string.IsNullOrWhiteSpace(provider.ReadinessPath))
        {
            throw new InvalidOperationException(
                $"Serverless provider '{provider.Name}' is missing ReadinessPath; serverless execution requires a readiness endpoint.");
        }

        return new ProviderExecutionPolicy(
            executionClass,
            provider.TimeoutSeconds,
            provider.MaximumActiveRequests,
            provider.QueueCapacity,
            provider.ReadinessPath,
            provider.ReadinessSuccessContractJson);
    }
}