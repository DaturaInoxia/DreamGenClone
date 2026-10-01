using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The trigger-token prepend, shared by every render path that applies a character LoRA.
///
/// <para>
/// This is load-bearing rather than cosmetic: without the token the graph happily loads the LoRA and renders a
/// stranger - a failure indistinguishable from success. It is pinned here as ONE implementation because two paths use
/// it now, and two copies would eventually disagree about the separator, the order, or whether to trim.
/// </para>
/// </summary>
public sealed class CharacterLoraPromptTokensTests
{
    [Fact]
    public void Prepend_StatesTheTokensAheadOfThePromptInChainOrder()
    {
        var loras = new[] { Lora("becky"), Lora("dean") };

        var result = CharacterLoraPromptTokens.Prepend("a woman on a bed", loras);

        Assert.Equal("becky, dean, a woman on a bed", result);
    }

    [Fact]
    public void Prepend_TrimsEachToken()
    {
        var result = CharacterLoraPromptTokens.Prepend("a woman", [Lora("  becky  ")]);

        Assert.Equal("becky, a woman", result);
    }

    /// <summary>
    /// A resolved LoRA with no trigger token contributes nothing rather than a stray separator - an artifact that
    /// declares no token is a fact about the artifact, not a cue to invent one.
    /// </summary>
    [Fact]
    public void Prepend_LeavesThePromptUnchangedWhenNoTokenIsDeclared()
    {
        Assert.Equal("a woman on a bed", CharacterLoraPromptTokens.Prepend("a woman on a bed", [Lora("   ")]));
        Assert.Equal("a woman on a bed", CharacterLoraPromptTokens.Prepend("a woman on a bed", []));
    }

    private static ResolvedCharacterLora Lora(string token) => new(
        ArtifactId: $"artifact-{token.Trim()}",
        CharacterProfileId: "character-1",
        CharacterName: "Becky",
        TriggerToken: token,
        FileName: $"loras/{token.Trim()}.safetensors",
        Strength: 0.8,
        Sha256: "sha256-of-the-artifact");
}
