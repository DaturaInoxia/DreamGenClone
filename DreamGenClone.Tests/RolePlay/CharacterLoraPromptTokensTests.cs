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

    /// <summary>
    /// The token is stated by this method and nowhere else, so a description that already opens with it has had the
    /// token applied twice. That is exactly what a round-trip used to do - it restored the compiled prompt (token
    /// included) beside the LoRA selection that put the token there - and the doubled token reached the model, which
    /// is why this refuses instead of de-duplicating in silence.
    /// </summary>
    [Fact]
    public void Prepend_RefusesAPromptThatAlreadyOpensWithTheTokenBeingApplied()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CharacterLoraPromptTokens.Prepend("ohwx-becky, a woman on a bed", [Lora("ohwx-becky")]));

        Assert.Contains("ohwx-becky", exception.Message, StringComparison.Ordinal);
        Assert.Contains("twice", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A DIFFERENT character's token ahead of the text is legitimate - that is a multi-character frame - so only the
    /// token being applied is refused, and only at the front.
    /// </summary>
    [Fact]
    public void Prepend_StillStatesADifferentTokenAheadOfAnExistingOne()
    {
        Assert.Equal(
            "dean, becky, a woman on a bed",
            CharacterLoraPromptTokens.Prepend("becky, a woman on a bed", [Lora("dean")]));
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
