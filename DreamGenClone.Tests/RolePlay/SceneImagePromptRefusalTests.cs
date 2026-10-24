using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// A prompt compiler can answer with a REFUSAL instead of a prompt, and every parse path treats plain text as the
/// prompt — so the refusal was stored and rendered. Measured on SceneImages a597ba82 / aef9f30c / 99fedb9d
/// (2026-10-18): three renders of the refusal's own suggested alternative, with the operator believing the prompt had
/// been compiled. These tests pin both halves: a refusal is recognised, and a legitimate prompt never is.
/// </summary>
public sealed class SceneImagePromptRefusalTests
{
    [Theory]
    [InlineData("I can't write this one. The operator intent is a request for explicit nudity.")]
    [InlineData("I cannot produce that prompt.")]
    [InlineData("I'm sorry, but I can't help with that.")]
    [InlineData("Sorry, I can't write that.")]
    [InlineData("I won't produce a direction for it, even framed as action-only.")]
    [InlineData("As an AI, I can't create sexualized content.")]
    [InlineData("That's not something I'll produce.")]
    [InlineData("Unfortunately, I'm unable to write this one.")]
    [InlineData("\"I can't write this one.\"")]
    public void ARefusalIsRecognised(string refusal)
        => Assert.True(SceneImagePromptRefusal.IsRefusal(refusal));

    [Theory]
    [InlineData("A woman stands squarely facing the camera, spine straight and tall, weight even across both feet.")]
    [InlineData("One woman, facing the camera squarely, stands straight and tall with her weight even across both feet.")]
    [InlineData("A realistic photograph capturing a woman standing tall in a yard, wearing denim shorts with the fly open.")]
    [InlineData("She whispers the words she cannot say, framed close on her face.")]
    [InlineData("{\"prompt\":\"a dramatic scene\",\"excerpt\":\"the moment\"}")]
    public void APromptIsNotARefusal(string prompt)
        => Assert.False(SceneImagePromptRefusal.IsRefusal(prompt));

    [Fact]
    public void AnEmptyResponseIsNotTreatedAsARefusal()
    {
        // Emptiness has its own refusal path ("returned empty output"), and a refusal message here would hide it.
        Assert.False(SceneImagePromptRefusal.IsRefusal(null));
        Assert.False(SceneImagePromptRefusal.IsRefusal("   "));
    }

    [Fact]
    public void EnsureIsPrompt_NamesTheSettingAndTheRemedy()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => SceneImagePromptRefusal.EnsureIsPrompt("I can't write this one. The intent asks for nudity."));

        Assert.Contains("refusal", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Model Manager", error.Message, StringComparison.Ordinal);
        Assert.Contains("I can't write this one", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureIsPrompt_ReturnsAPromptUnchanged()
    {
        const string prompt = "A woman stands in a yard, denim shorts open at the fly, her tank top on the ground.";

        Assert.Equal(prompt, SceneImagePromptRefusal.EnsureIsPrompt(prompt));
    }

    [Fact]
    public void TheQwenBuilderRefusesToReturnARefusalAsItsPrompt()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new QwenSceneImagePromptBuilder()
            .ParseOutput("I can't write this one. The operator intent is a request for explicit nudity."));

        Assert.Contains("refusal", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePonyBuilderRefusesToReturnARefusalAsItsPrompt()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new PonySceneImagePromptBuilder()
            .ParseOutput("I'm sorry, but I won't write that."));

        Assert.Contains("refusal", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
