using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// A preset is assembled deterministically, so the one thing that can go wrong is the assembly: a slot nobody
/// fills, a preserve clause that never reaches the instruction, or a clause attached to a mode that has nothing to
/// preserve. Each of those sends an instruction that looks plausible and is wrong, so each is refused here.
/// </summary>
public sealed class ImagePresetInstructionComposerTests
{
    private const string LightingChange =
        "Relight this photograph to the following lighting: {Detail} {Preserve}";

    private const string LightingCondition = "The scene is lit by {Detail}";

    private const string ExpressionChange =
        "Change the subject's facial expression only. The new expression is exactly this: {Detail} {Preserve}";

    private const string ExpressionCondition = "with {Detail}";

    private const string Detail = "dim, low-key indoor light from one warm lamp just outside the frame to camera left";

    private const string Preserve =
        "Keep the person identical - the same face, body, skin, hair and marks - and keep the pose, camera angle, "
        + "framing, crop, clothing and setting unchanged; only the lighting changes.";

    [Fact]
    public void Compose_EditInstruction_CarriesTheDetailAndThePreserveClause()
    {
        var instruction = ImagePresetInstructionComposer.Compose(
            ImagePresetAxis.Lighting, ImagePresetMode.Change, Detail, LightingChange, Preserve);

        Assert.Contains(Detail, instruction, StringComparison.Ordinal);
        Assert.Contains("Keep the person identical", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("{", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_ComposeClause_CarriesTheDetailAndNoPreserveClause()
    {
        var clause = ImagePresetInstructionComposer.Compose(
            ImagePresetAxis.Lighting, ImagePresetMode.Condition, Detail, LightingCondition);

        Assert.Equal($"The scene is lit by {Detail}", clause);
    }

    /// <summary>An edit that changes one thing without stating what stays identical re-renders the person.</summary>
    [Fact]
    public void Compose_EditWithoutAPreserveClause_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ImagePresetInstructionComposer.Compose(
            ImagePresetAxis.Expression, ImagePresetMode.Change, Detail, ExpressionChange));

        Assert.Contains(ImagePresetKeys.PreserveExpression, error.Message, StringComparison.Ordinal);
    }

    /// <summary>A compose step has nothing to preserve, so a preserve slot in its assembly would be dropped silently.</summary>
    [Fact]
    public void Compose_ConditionAssemblyWithAPreserveSlot_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ImagePresetInstructionComposer.Compose(
            ImagePresetAxis.Lighting, ImagePresetMode.Condition, Detail, LightingCondition + " {Preserve}", Preserve));

        Assert.Contains(ImagePresetKeys.PreserveSlot, error.Message, StringComparison.Ordinal);
    }

    /// <summary>An assembly with no detail slot could never carry the preset's own wording.</summary>
    [Fact]
    public void Compose_AssemblyWithoutADetailSlot_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ImagePresetInstructionComposer.Compose(
            ImagePresetAxis.Lighting, ImagePresetMode.Condition, Detail, "The scene is lit by a lamp."));

        Assert.Contains(ImagePresetKeys.DetailSlot, error.Message, StringComparison.Ordinal);
    }

    /// <summary>An unknown slot is a template that has drifted from the composer; it must not reach the model.</summary>
    [Fact]
    public void Compose_AssemblyWithAnUnknownSlot_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ImagePresetInstructionComposer.Compose(
            ImagePresetAxis.Lighting, ImagePresetMode.Condition, Detail, "The scene is lit by {Detail} {Mood}"));

        Assert.Contains("{Mood}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_WithoutADetail_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ImagePresetInstructionComposer.Compose(
            ImagePresetAxis.Lighting, ImagePresetMode.Condition, "  ", LightingCondition));

        Assert.Contains("detail", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An expression change reads as a sentence, and the preserve clause is the expression one.</summary>
    [Fact]
    public void Compose_ExpressionChange_NamesTheExpressionAndPreservesEverythingElse()
    {
        var instruction = ImagePresetInstructionComposer.Compose(
            ImagePresetAxis.Expression, ImagePresetMode.Change,
            "the eyebrows pulled down and drawn together with vertical creases between them",
            ExpressionChange,
            "Keep the person identical; only the facial expression changes.");

        Assert.Contains("facial expression", instruction, StringComparison.Ordinal);
        Assert.Contains("vertical creases", instruction, StringComparison.Ordinal);
        Assert.Contains("only the facial expression changes", instruction, StringComparison.Ordinal);
    }

    /// <summary>A condition clause appends to a render prompt, so it must not be a sentence of its own.</summary>
    [Fact]
    public void Compose_ConditionClause_ReadsAsAPhrase()
    {
        var clause = ImagePresetInstructionComposer.Compose(
            ImagePresetAxis.Expression, ImagePresetMode.Condition,
            "the eyes half-lidded with a heavy downward gaze", ExpressionCondition);

        Assert.Equal("with the eyes half-lidded with a heavy downward gaze", clause);
    }

    /// <summary>The axis and the mode choose the assembly template, and an impossible pair is refused by name.</summary>
    [Fact]
    public void AssemblyKey_FollowsTheAxisAndTheMode()
    {
        Assert.Equal(
            ImagePresetKeys.AssemblyLightingChange,
            ImagePresetKeys.AssemblyKey(ImagePresetAxis.Lighting, ImagePresetMode.Change));
        Assert.Equal(
            ImagePresetKeys.AssemblyExpressionCondition,
            ImagePresetKeys.AssemblyKey(ImagePresetAxis.Expression, ImagePresetMode.Condition));

        Assert.Throws<InvalidOperationException>(
            () => ImagePresetKeys.AssemblyKey((ImagePresetAxis)99, ImagePresetMode.Change));
        Assert.Throws<InvalidOperationException>(
            () => ImagePresetKeys.AssemblyKey(ImagePresetAxis.Lighting, (ImagePresetMode)99));
    }

    /// <summary>An axis is read back off the key, so a caller cannot pass a lighting key with an expression axis.</summary>
    [Fact]
    public void AxisOf_ReadsTheAxisOffTheKey()
    {
        Assert.Equal(ImagePresetAxis.Lighting, ImagePresetKeys.AxisOf(ImagePresetKeys.LightingIndoorDim));
        Assert.Equal(ImagePresetAxis.Expression, ImagePresetKeys.AxisOf(ImagePresetKeys.ExpressionAngry));

        var error = Assert.Throws<InvalidOperationException>(
            () => ImagePresetKeys.AxisOf(LoraCellWorkflowKeys.VocabularyOutfitCasual));
        Assert.Contains("not a preset key", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Short names are for display only, and they are what a picker shows next to a picked preset.</summary>
    [Fact]
    public void ShortName_IsTheValueUnderEitherNamespace()
    {
        Assert.Equal("indoor-dim", ImagePresetKeys.ShortName(ImagePresetKeys.LightingIndoorDim));
        Assert.Equal("indoor-dim", ImagePresetKeys.ShortName(LoraCellWorkflowKeys.VocabularyLightingIndoorDim));
        Assert.Equal("angry", ImagePresetKeys.ShortName(ImagePresetKeys.ExpressionAngry));
        Assert.Equal(string.Empty, ImagePresetKeys.ShortName(null));
    }
}
