using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.ModelManager;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// <see cref="PoseImageModelResolver"/> decides whether a pose (OpenPose ControlNet) render may be queued, and it is
/// the SAME resolver the character-body panel asks to decide whether to offer the option at all — so "offered" and
/// "renderable" cannot disagree.
///
/// The rule these tests pin: permission comes from the model's DECLARED strategy and its passing qualification, while
/// the family only decides WHICH GRAPH the client will build. A family with no graph is refused even when qualified,
/// because passing the gate and then failing at render time is worse than a refusal that names the remedy.
/// </summary>
public sealed class PoseImageModelResolverTests
{
    private const string ControlNetStrategy = "PoseControlNet";

    private const string LocalProviderId = "provider-local-comfyui";

    private static RegisteredModel Model(SceneImageModelFamily family, string qualification, string? strategies = null)
        => new()
        {
            Id = $"model-{family}",
            ProviderId = LocalProviderId,
            DisplayName = $"Test {family}",
            ModelIdentifier = family == SceneImageModelFamily.Flux
                ? "flux1-dev-fp8.safetensors"
                : "juggernautXL_ragnarok.safetensors",
            ModelKind = ModelKind.Image,
            IsEnabled = true,
            SceneImageModelFamily = family,
            SupportedVisualStrategiesJson = strategies ?? $"[\"{ControlNetStrategy}\"]",
            CapabilityQualificationsJson = qualification
        };

    private static Provider Provider() => new()
    {
        Id = LocalProviderId,
        Name = "Local ComfyUI (test)",
        BaseUrl = "http://localhost:8188",
        ImageCapability = ImageProviderCapability.ImageOnly,
        ContentPolicy = ImageContentPolicy.AdultAllowed,
        ImageProtocol = ImageProtocol.ComfyUi,
        TimeoutSeconds = 600,
        IsEnabled = true
    };

    /// <summary>A qualification entry, PascalCase like the stored JSON.</summary>
    private static string Qualification(string? extra = null)
        => $"[{{\"Strategy\":\"{ControlNetStrategy}\",\"EndpointId\":\"{LocalProviderId}\",\"Qualified\":true,"
           + $"\"ProofId\":\"proof-1\",\"AdapterRef\":\"some-controlnet.safetensors\",\"DefaultStrength\":0.85"
           + (extra is null ? string.Empty : "," + extra) + "}]";

    /// <summary>Every reference the XLabs FLUX graph needs, as configured values.</summary>
    private static string FluxRefs(string? omit = null)
    {
        var fields = new (string Name, string Value)[]
        {
            ("UnetName", "\"flux1-dev-fp8.safetensors\""),
            ("ClipName1", "\"t5xxl_fp8_e4m3fn.safetensors\""),
            ("ClipName2", "\"clip_l.safetensors\""),
            ("VaeName", "\"ae.safetensors\""),
            ("ControlNetModelName", "\"flux-dev-fp8\""),
            ("Guidance", "3.5"),
            ("Steps", "28"),
            ("TimestepToStartCfg", "1")
        };

        return string.Join(",", fields
            .Where(field => !string.Equals(field.Name, omit, StringComparison.Ordinal))
            .Select(field => $"\"{field.Name}\":{field.Value}"));
    }

    private static Task<ResolvedPoseImageModel> ResolveAsync(RegisteredModel model)
    {
        var models = new StubModelRepository(model);
        var providers = new StubProviderRepository(Provider());
        var resolver = new PoseImageModelResolver(models, providers, NullLogger<PoseImageModelResolver>.Instance);
        return resolver.ResolveAsync(model.Id);
    }

    // ── SDXL: unchanged behaviour ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// SDXL resolves exactly as before and carries NO FLUX references — the two graphs do not share plumbing, so a
    /// FLUX-only field must never leak into the SDXL path.
    /// </summary>
    [Fact]
    public async Task Sdxl_WithAQualifiedStrategy_Resolves_WithNoFluxRefs()
    {
        var resolved = await ResolveAsync(Model(SceneImageModelFamily.Sdxl, Qualification()));

        Assert.Equal(SceneImageModelFamily.Sdxl, resolved.Family);
        Assert.Null(resolved.Flux);
        Assert.Equal("some-controlnet.safetensors", resolved.ControlNetAdapterRef);
        Assert.Equal(0.85, resolved.DefaultStrength);
    }

    // ── FLUX: the XLabs graph, from configured values only ─────────────────────────────────────────────────

    /// <summary>
    /// A FLUX model that declares and qualifies PoseControlNet now RESOLVES — this is the refusal that used to be
    /// hardcoded ("only qualified for SDXL-family models today") and made an installed, proven capability unreachable
    /// from the UI.
    /// </summary>
    [Fact]
    public async Task Flux_WithAQualifiedStrategyAndEveryReference_ResolvesTheXlabsGraph()
    {
        var resolved = await ResolveAsync(Model(SceneImageModelFamily.Flux, Qualification(FluxRefs())));

        Assert.Equal(SceneImageModelFamily.Flux, resolved.Family);
        var flux = Assert.IsType<FluxPoseRefs>(resolved.Flux);
        Assert.Equal("flux1-dev-fp8.safetensors", flux.UnetName);
        Assert.Equal("t5xxl_fp8_e4m3fn.safetensors", flux.ClipName1);
        Assert.Equal("clip_l.safetensors", flux.ClipName2);
        Assert.Equal("ae.safetensors", flux.VaeName);
        Assert.Equal("flux-dev-fp8", flux.ControlNetModelName);
        Assert.Equal(3.5, flux.Guidance);
        Assert.Equal(28, flux.Steps);
        Assert.Equal(1, flux.TimestepToStartCfg);
    }

    /// <summary>
    /// Every FLUX reference is required, and a missing one is refused BY NAME rather than defaulted. The graph loads
    /// each file from the ComfyUI host, so a substituted encoder or VAE would render a silently different picture
    /// instead of failing — which is the failure mode this whole contract exists to prevent.
    /// </summary>
    [Theory]
    [InlineData("UnetName")]
    [InlineData("ClipName1")]
    [InlineData("ClipName2")]
    [InlineData("VaeName")]
    [InlineData("ControlNetModelName")]
    [InlineData("Guidance")]
    [InlineData("Steps")]
    [InlineData("TimestepToStartCfg")]
    public async Task Flux_MissingAConfiguredReference_IsRefusedNamingTheField(string missing)
    {
        var error = await Assert.ThrowsAsync<ModelResolutionException>(
            () => ResolveAsync(Model(SceneImageModelFamily.Flux, Qualification(FluxRefs(missing)))));

        Assert.Contains(missing, error.Message, StringComparison.Ordinal);
        Assert.Contains("Model Manager", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A FLUX model that declares the strategy but has no passing proof is still refused.</summary>
    [Fact]
    public async Task Flux_WithoutAPassingProof_IsRefused()
    {
        var unqualified = $"[{{\"Strategy\":\"{ControlNetStrategy}\",\"EndpointId\":\"{LocalProviderId}\","
                          + $"\"Qualified\":false,\"ProofId\":\"\",\"AdapterRef\":\"cn.safetensors\",\"DefaultStrength\":0.85,"
                          + FluxRefs() + "}]";

        var error = await Assert.ThrowsAsync<ModelResolutionException>(
            () => ResolveAsync(Model(SceneImageModelFamily.Flux, unqualified)));

        Assert.Contains(ControlNetStrategy, error.Message, StringComparison.Ordinal);
    }

    // ── The family that has no graph ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pony is SDXL-BASED, so it is tempting to let it inherit the SDXL graph. It is refused instead, because no Pony
    /// pose render has been proven: this asserts the refusal comes from "there is no graph for this family", NOT from
    /// the old blanket "only SDXL-family models" rule — and that it names the proven graphs so the remedy is obvious.
    /// </summary>
    [Fact]
    public async Task Pony_IsRefused_BecauseNoGraphExistsForTheFamily_NotBecauseItIsUnqualified()
    {
        var error = await Assert.ThrowsAsync<ModelResolutionException>(
            () => ResolveAsync(Model(SceneImageModelFamily.Pony, Qualification())));

        Assert.Contains("No pose-conditioned (OpenPose) graph exists", error.Message, StringComparison.Ordinal);
        Assert.Contains("Pony", error.Message, StringComparison.Ordinal);
        Assert.Contains("SDXL (thibaud OpenPoseXL2) and FLUX (XLabs OpenPose)", error.Message, StringComparison.Ordinal);
    }

    // ── Stubs ──────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class StubModelRepository(RegisteredModel model) : IRegisteredModelRepository
    {
        public Task<RegisteredModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(id == model.Id ? model : null);

        public Task<RegisteredModel> SaveAsync(RegisteredModel value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<List<RegisteredModel>> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<List<RegisteredModel>> GetAllEnabledAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<bool> ExistsByProviderAndIdentifierAsync(string providerId, string modelIdentifier, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubProviderRepository(Provider provider) : IProviderRepository
    {
        public Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(id == provider.Id ? provider : null);

        public Task<Provider> SaveAsync(Provider value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<List<Provider>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
