using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Applying a render's selected character LoRAs. What is pinned here is that the CHAIN and the TRIGGER TOKENS are one
/// decision: a LoRA loaded without its token renders a stranger, which looks exactly like a successful render, and a
/// token with no LoRA names a character that nothing binds to.
/// </summary>
public sealed class CharacterLoraRenderApplicationTests
{
    [Fact]
    public async Task ApplyAsync_WithNoSelection_LeavesTheRenderExactlyAsItWas()
    {
        var model = Model();

        var applied = await CharacterLoraRenderApplication.ApplyAsync(
            model, "a woman on a bed", selections: null, new StubResolver([]));

        Assert.Same(model, applied.Model);
        Assert.Null(applied.Model.Loras);
        Assert.Equal("a woman on a bed", applied.Prompt);
    }

    [Fact]
    public async Task ApplyAsync_AppliesTheChainToTheModelAndStatesTheTokensInThePrompt()
    {
        var applied = await CharacterLoraRenderApplication.ApplyAsync(
            Model(),
            "a woman on a bed",
            [Selection("artifact-1")],
            new StubResolver([Lora("becky", "artifact-1")]));

        var lora = Assert.Single(applied.Model.Loras!);
        Assert.Equal("artifact-1", lora.ArtifactId);
        Assert.Equal("becky, a woman on a bed", applied.Prompt);
    }

    [Fact]
    public async Task ApplyAsync_KeepsTheSelectedLorasInChainOrder()
    {
        var applied = await CharacterLoraRenderApplication.ApplyAsync(
            Model(),
            "two people on a bed",
            [Selection("artifact-1"), Selection("artifact-2")],
            new StubResolver([Lora("becky", "artifact-1"), Lora("dean", "artifact-2")]));

        Assert.Equal(["becky", "dean"], applied.Model.Loras!.Select(lora => lora.TriggerToken));
        Assert.Equal("becky, dean, two people on a bed", applied.Prompt);
    }

    /// <summary>
    /// Rendering the character WITHOUT their identity would produce a different person while looking successful, so a
    /// selection with no resolver to honour it is refused rather than quietly dropped.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_RefusesASelectionItCannotHonour()
    {
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CharacterLoraRenderApplication.ApplyAsync(
                Model(), "a woman on a bed", [Selection("artifact-1")], resolver: null));

        Assert.Contains("different person", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A selection that resolves to nothing is not half-applied: no chain means no trigger tokens either, so the prompt
    /// keeps naming nobody rather than naming a character nothing binds to.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_IsAllOrNothingWhenNothingResolves()
    {
        var applied = await CharacterLoraRenderApplication.ApplyAsync(
            Model(), "a woman on a bed", [Selection("artifact-1")], new StubResolver([]));

        Assert.Null(applied.Model.Loras);
        Assert.Equal("a woman on a bed", applied.Prompt);
    }

    private static ResolvedImageModel Model() => new(
        ProviderBaseUrl: "http://localhost:8188",
        ImageGenerationPath: "/prompt",
        ProviderTimeoutSeconds: 30,
        ApiKeyEncrypted: null,
        ModelIdentifier: "bigLust_v16.safetensors",
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        ProviderName: "Local ComfyUI",
        IsSessionOverride: false,
        SceneImageModelFamily: SceneImageModelFamily.Sdxl,
        PromptDialect: SceneImagePromptDialect.SdxlNaturalLanguage,
        ImageProtocol: ImageProtocol.ComfyUi);

    private static SceneImageCharacterLoraSelection Selection(string artifactId) =>
        new() { ArtifactId = artifactId };

    private static ResolvedCharacterLora Lora(string token, string artifactId) => new(
        ArtifactId: artifactId,
        CharacterProfileId: "character-1",
        CharacterName: "Becky",
        TriggerToken: token,
        FileName: $"loras/{artifactId}.safetensors",
        Strength: 0.8,
        Sha256: "sha256-of-the-artifact");

    private sealed class StubResolver(IReadOnlyList<ResolvedCharacterLora> loras) : ISceneImageCharacterLoraResolver
    {
        public Task<IReadOnlyList<ResolvedCharacterLora>> ResolveAsync(
            ResolvedImageModel model,
            SceneImageStudioSettings? settings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResolvedCharacterLora>>(
                settings?.CharacterLoras is { Count: > 0 } ? loras : []);
    }
}
