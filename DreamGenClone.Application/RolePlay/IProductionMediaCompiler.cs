using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public sealed record ProductionMediaCompilerDescriptor(
    string CompilerId,
    string CompilerVersion,
    MediaOperation Operation);

public sealed record ProductionMediaCompilationInput(
    string RequestId,
    ProductionIntentSnapshot Intent,
    MediaCapabilityProfile CapabilityProfile,
    MediaCapabilityCell CapabilityCell,
    string SettingsJson,
    IReadOnlyList<OrderedMediaReferenceBinding> ReferenceBindings,
    DateTime CreatedUtc);

public sealed record ProductionMediaCompilation(
    CompiledMediaRequest Request,
    IReadOnlyList<OrderedMediaReferenceBinding> ReferenceBindings);

public interface IProductionMediaCompiler
{
    ProductionMediaCompilerDescriptor Descriptor { get; }
    ProductionMediaCompilation Compile(ProductionMediaCompilationInput input);
}

public interface IProductionMediaCompilerRegistry
{
    IProductionMediaCompiler Resolve(MediaCapabilityProfile profile);
}

public interface IProductionMediaCompilationService
{
    Task<ProductionMediaCompilation> CompileAndPersistAsync(
        string requestId,
        string intentId,
        string capabilityProfileId,
        string capabilityCellId,
        string settingsJson,
        IReadOnlyList<OrderedMediaReferenceBinding> referenceBindings,
        DateTime createdUtc,
        CancellationToken cancellationToken = default);

    Task<ProductionMediaCompilation> CompileIdentityAndPersistAsync(
        string requestId,
        string intentId,
        string capabilityProfileId,
        string capabilityCellId,
        string settingsJson,
        IReadOnlyList<OrderedMediaReferenceBinding> referenceBindings,
        IReadOnlyList<IdentityStrategyBinding> identityBindings,
        DateTime createdUtc,
        CancellationToken cancellationToken = default);
}