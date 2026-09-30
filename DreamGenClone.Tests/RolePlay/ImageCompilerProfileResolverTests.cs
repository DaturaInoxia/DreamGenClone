using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 B135-003 — resolution is keyed on the CHECKPOINT, and a checkpoint with no profile is refused by name.
///
/// <para>
/// The point of these tests is the refusal. The family-keyed registry could not tell BigLust from Juggernaut and
/// silently compiled Qwen-2.1 and FLUX with the SDXL-branded builder, because a family lookup always finds
/// <em>something</em>. A checkpoint lookup that finds nothing must say so.
/// </para>
/// </summary>
public sealed class ImageCompilerProfileResolverTests
{
    private static ImageCompilerProfileResolver NewResolver(string dbPath) =>
        new(new ImageCompilerProfileRepository(
            Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" })));

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"b135-resolver-{Guid.NewGuid():N}.db");

    private static ResolvedImageModel Model(string modelIdentifier, string providerName = "Local ComfyUI") => new(
        ProviderBaseUrl: "http://localhost:8188",
        ImageGenerationPath: "/prompt",
        ProviderTimeoutSeconds: 120,
        ApiKeyEncrypted: null,
        ModelIdentifier: modelIdentifier,
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        ProviderName: providerName,
        IsSessionOverride: false,
        SceneImageModelFamily: SceneImageModelFamily.Sdxl,
        PromptDialect: SceneImagePromptDialect.SdxlNaturalLanguage,
        ImageProtocol: ImageProtocol.ComfyUi);

    [Fact]
    public async Task Resolve_ReturnsTheProfileForTheCheckpointTheRenderLandedOn()
    {
        var resolver = NewResolver(NewDbPath());

        var profile = await resolver.ResolveAsync(Model("bigLust_v16.safetensors"));

        Assert.Equal("BigLust v1.6", profile.DisplayName);
        Assert.Equal(SceneImageModelFamily.Sdxl, profile.Family);
    }

    [Fact]
    public async Task Resolve_DistinguishesTwoCheckpointsInTheSameFamily()
    {
        var resolver = NewResolver(NewDbPath());

        var bigLust = await resolver.ResolveAsync(Model("bigLust_v16.safetensors"));
        var juggernaut = await resolver.ResolveAsync(Model("juggernautXL_ragnarok.safetensors"));

        // Both are SDXL, so the family key could not tell them apart — but they are different rows with different
        // envelopes (BigLust 1024x1024; Juggernaut 832x1216 portrait).
        Assert.NotEqual(bigLust.Id, juggernaut.Id);
        Assert.NotEqual(bigLust.SettingsEnvelopeJson, juggernaut.SettingsEnvelopeJson);
    }

    [Fact]
    public async Task Resolve_IsCaseInsensitive()
    {
        var resolver = NewResolver(NewDbPath());

        var profile = await resolver.ResolveAsync("JUGGERNAUTXL_RAGNAROK.SAFETENSORS", "test", CancellationToken.None);

        Assert.Equal("Juggernaut XL Ragnarok", profile.DisplayName);
    }

    [Fact]
    public async Task Resolve_RefusesACheckpointWithNoProfile_AndNamesIt()
    {
        var resolver = NewResolver(NewDbPath());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(Model("some-new-checkpoint.safetensors", "Local ComfyUI")));

        Assert.Contains("some-new-checkpoint.safetensors", error.Message);
        Assert.Contains("Local ComfyUI", error.Message);
        // The refusal must be explicit that no family default was substituted.
        Assert.Contains("never falls back", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolve_RefusesAModelWithNoCheckpointIdentifier()
    {
        var resolver = NewResolver(NewDbPath());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(Model("   ")));

        Assert.Contains("no checkpoint identifier", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolve_RequiresACheckpointIdentifier()
    {
        var resolver = NewResolver(NewDbPath());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("  ", "test", CancellationToken.None));
    }

    [Fact]
    public async Task Registry_ResolvesACompilerFromAProfile_AndReportsTheCheckpointWhenNoneFits()
    {
        var resolver = NewResolver(NewDbPath());
        var registry = new SceneImagePromptCompilerRegistry(
        [
            new FakeCompiler(SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage),
        ]);

        var sdxlProfile = await resolver.ResolveAsync("bigLust_v16.safetensors");
        var compiler = registry.Resolve(sdxlProfile);

        Assert.Equal(SceneImageModelFamily.Sdxl, compiler.Family);

        // A Pony profile against a registry holding no Pony compiler reports the CHECKPOINT, not just a family.
        var ponyProfile = await resolver.ResolveAsync("ponyDiffusionV6XL_v6.safetensors");
        var error = Assert.Throws<InvalidOperationException>(() => { registry.Resolve(ponyProfile); });

        Assert.Contains("ponyDiffusionV6XL_v6.safetensors", error.Message);
    }

    private sealed class FakeCompiler(SceneImageModelFamily family, SceneImagePromptDialect dialect) : ISceneImagePromptCompiler
    {
        public SceneImageModelFamily Family { get; } = family;

        public SceneImagePromptDialect PromptDialect { get; } = dialect;

        public ISceneImageLLMPromptBuilder PromptBuilder => throw new NotSupportedException();

        public string CanonicalNegativePrompt => string.Empty;

        public string BuildNegativePrompt(SceneImageBeat beat, string pov) => string.Empty;
    }
}
