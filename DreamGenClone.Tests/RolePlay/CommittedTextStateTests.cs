namespace DreamGenClone.Tests.RolePlay;

using DreamGenClone.Web.Application.TextEntry;

/// <summary>
/// The rule behind <c>CommittedTextInput</c>, which every text box in the app now draws through.
///
/// <para>
/// These are the cases that decide whether typing is safe: a keystroke must NOT move the text the box draws (that is
/// what makes a render unable to write a stale value into a box someone is typing in), the host's own new value MUST
/// move it (a seed, a draft load, a compiled prompt, a clear), and the host's verbatim echo of a keystroke must be
/// ignored (adopting an echo would re-draw the box on every render - the very failure this exists to prevent).
/// </para>
/// </summary>
public sealed class CommittedTextStateTests
{
    [Fact]
    public void FirstParameterSet_AdoptsTheHostsText()
    {
        var state = new CommittedTextState();

        Assert.True(state.AdoptHostText("Maintenance Shed"));
        Assert.Equal("Maintenance Shed", state.Rendered);
    }

    [Fact]
    public void AKeystroke_ReportsToTheHostWithoutMovingTheDrawnText()
    {
        var state = new CommittedTextState();
        state.AdoptHostText("shed");

        state.Report("shed on");
        state.Report("shed on the left");

        Assert.Equal("shed on the left", state.Reported);
        Assert.Equal("shed", state.Rendered);
    }

    [Fact]
    public void TheHostsEchoOfAKeystroke_IsNotTreatedAsANewValue()
    {
        var state = new CommittedTextState();
        state.AdoptHostText("shed");
        state.Report("shed on");

        Assert.False(state.AdoptHostText("shed on"));
        Assert.Equal("shed", state.Rendered);
    }

    [Fact]
    public void AValueTheOperatorDidNotType_MovesTheDrawnText()
    {
        var state = new CommittedTextState();
        state.AdoptHostText("old");
        state.Report("old typed");

        // A compile, a draft load, a clear: the host is not echoing, so the box must show it.
        Assert.True(state.AdoptHostText("the compiled prompt"));
        Assert.Equal("the compiled prompt", state.Rendered);
    }

    [Fact]
    public void ACommit_MakesTheOperatorsTextTheDrawnText()
    {
        var state = new CommittedTextState();
        state.AdoptHostText("shed");
        state.Report("shed on the left");

        state.Commit("shed on the left");

        Assert.Equal("shed on the left", state.Rendered);
        Assert.False(state.AdoptHostText("shed on the left"));
    }

    [Fact]
    public void Null_IsTreatedAsEmptyText()
    {
        var state = new CommittedTextState();
        state.AdoptHostText("shed");

        state.Commit(null);

        Assert.Equal(string.Empty, state.Rendered);
        Assert.Equal(string.Empty, state.Reported);
        Assert.False(state.AdoptHostText(null));
    }
}
