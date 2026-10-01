using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Preset clause assembly (B-133). What is pinned here is that a preset REPLACES its axis's wording and never sits
/// beside it: two lighting clauses in one prompt contradict each other, and the operator picked a second condition
/// precisely because the first was wrong.
/// </summary>
public sealed class ImagePresetClauseEditorTests
{
    private const string Dim = "dim, low-key indoor light from one warm lamp just outside the frame";
    private const string Bright = "bright, even daylight filling the room from a large window";

    [Fact]
    public void Apply_WithNoPreviousClause_AddsTheClauseToThePrompt()
    {
        var result = ImagePresetClauseEditor.Apply("a woman on a bed", null, Dim, ImagePresetAxis.Lighting);

        Assert.Equal($"a woman on a bed {Dim}", result);
    }

    /// <summary>A preset on an empty prompt IS the prompt - there is nothing to join it to.</summary>
    [Fact]
    public void Apply_WithAnEmptyPrompt_MakesTheClauseTheWholePrompt()
    {
        Assert.Equal(Dim, ImagePresetClauseEditor.Apply(string.Empty, null, Dim, ImagePresetAxis.Lighting));
        Assert.Equal(Dim, ImagePresetClauseEditor.Apply("   ", null, Dim, ImagePresetAxis.Lighting));
    }

    /// <summary>
    /// The whole point: the second pick must not leave the first one's wording behind, and must leave exactly one
    /// copy of the new wording.
    /// </summary>
    [Fact]
    public void Apply_WithAPreviousClause_ReplacesItRatherThanStackingBesideIt()
    {
        var first = ImagePresetClauseEditor.Apply("a woman on a bed", null, Dim, ImagePresetAxis.Lighting);

        // The second pick is applied to the prompt the FIRST one produced - which is the real sequence: the panel
        // passes back the clause it inserted, and that clause is by definition in the prompt it inserted it into.
        var second = ImagePresetClauseEditor.Apply(first, Dim, Bright, ImagePresetAxis.Lighting);

        Assert.Equal($"a woman on a bed {Dim}", first);
        Assert.Equal($"a woman on a bed {Bright}", second);

        // The previous wording is gone, and the new one appears exactly once.
        Assert.DoesNotContain(Dim, second, StringComparison.Ordinal);
        Assert.Equal(1, second.Split(Bright, StringSplitOptions.None).Length - 1);
    }

    /// <summary>Replacing does not disturb the operator's own wording, or the other axis's clause.</summary>
    [Fact]
    public void Apply_LeavesTheOperatorsTextAndTheOtherAxisAlone()
    {
        const string expression = "a faint knowing smile";
        var prompt = ImagePresetClauseEditor.Apply("a woman on a bed", null, expression, ImagePresetAxis.Expression);

        var result = ImagePresetClauseEditor.Apply(prompt, null, Dim, ImagePresetAxis.Lighting);

        Assert.Contains("a woman on a bed", result, StringComparison.Ordinal);
        Assert.Contains(expression, result, StringComparison.Ordinal);
        Assert.Contains(Dim, result, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal that keeps the conflict out: when the wording this preset would replace has been edited away, the
    /// app cannot find it and must NOT add a second clause beside one it cannot read.
    /// </summary>
    [Fact]
    public void Apply_WhenThePreviousClauseIsGone_RefusesAndNamesTheAxis()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            ImagePresetClauseEditor.Apply("a woman on a bed, lit warmly", Dim, Bright, ImagePresetAxis.Lighting));

        Assert.Contains("Lighting", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("no longer in the prompt", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_RefusesABlankClause()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ImagePresetClauseEditor.Apply("a woman", null, "   ", ImagePresetAxis.Lighting));
    }

    [Fact]
    public void Remove_TakesTheClauseAndItsLeadingSpaceWithIt()
    {
        var prompt = $"a woman on a bed {Dim}";

        var result = ImagePresetClauseEditor.Remove(prompt, Dim);

        Assert.Equal("a woman on a bed", result);
        Assert.DoesNotContain(Dim, result, StringComparison.Ordinal);
    }

    /// <summary>Removing a clause that is not there changes nothing - reverting must not fail.</summary>
    [Fact]
    public void Remove_IsANoOpWhenTheClauseIsAbsent()
    {
        Assert.Equal("a woman on a bed", ImagePresetClauseEditor.Remove("a woman on a bed", Dim));
        Assert.Equal(string.Empty, ImagePresetClauseEditor.Remove(string.Empty, Dim));
    }

    /// <summary>Reverting what was applied returns the prompt exactly to what it was.</summary>
    [Fact]
    public void Remove_UndoesApplyExactly()
    {
        const string original = "a woman on a bed, medium shot";

        var applied = ImagePresetClauseEditor.Apply(original, null, Dim, ImagePresetAxis.Lighting);

        Assert.Equal(original, ImagePresetClauseEditor.Remove(applied, Dim));
    }
}
