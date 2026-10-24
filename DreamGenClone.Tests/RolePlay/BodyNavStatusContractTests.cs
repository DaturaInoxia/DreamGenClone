using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The Body tab's left navigation answers one question — "which of these cells are done, and which still need me?" —
/// so the statuses it shows have to come from the surface that already knows them, and mean the same thing there as
/// in the panel beside it.
///
/// <para>
/// Two failure modes are pinned here. First, a SECOND loader: the navigation could query the view rows itself, and
/// then a cell the operator just accepted would still read "Not started" on the left while the panel on the right said
/// otherwise — a list that answers the wrong question is worse than no list. Second, a SECOND status vocabulary: the
/// panel already had a colour switch, and a second one would let the same view read green in one place and amber in
/// the other. Both are asserted structurally, because neither is visible in a passing render.
/// </para>
/// </summary>
public sealed class BodyNavStatusContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string StudioSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor"));
    private static readonly string PanelSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor"));
    private static readonly string DisplaySource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Application", "RolePlay", "BodyViewStatusDisplay.cs"));

    [Fact]
    public void TheNavMirrorsThePanelsRows_InsteadOfLoadingItsOwn()
    {
        // The panel publishes what it already loads...
        Assert.Contains("public EventCallback<IReadOnlyList<CharacterIdentityBodyView>> ViewsChanged { get; set; }", PanelSource, StringComparison.Ordinal);
        Assert.Contains("await ViewsChanged.InvokeAsync(_views);", PanelSource, StringComparison.Ordinal);

        // ...every re-read goes through the one loader that publishes...
        Assert.Contains("private async Task LoadViewsAsync()", PanelSource, StringComparison.Ordinal);
        Assert.Contains("await LoadViewsAsync();", PanelSource, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(PanelSource, @"_views = \(await BodyService\.ListViewsAsync"));

        // ...and what the panel publishes WINS, so a cell the operator just accepted cannot still read "Not started"
        // on the left.
        Assert.Contains("ViewsChanged=\"OnBodyViewsChangedAsync\"", StudioSource, StringComparison.Ordinal);
        Assert.Contains("_bodyNavViews = views;", StudioSource, StringComparison.Ordinal);

        // The navigation ALSO reads the rows itself when the Body tab is resolved. It has to: the panel is mounted by
        // ONE section (body-angle), so a navigation that waited for it showed every cell as "Not started" until the
        // operator clicked one (reported 2026-10-11). The two sources agree because they are the same call, and the
        // panel's publish is the later word.
        Assert.Contains("private async Task RefreshBodyNavViewsAsync()", StudioSource, StringComparison.Ordinal);
        Assert.Contains("await RefreshBodyNavViewsAsync();", StudioSource, StringComparison.Ordinal);
        Assert.Contains("_bodyNavViews = (await BodyService.ListViewsAsync(_bodyBuild.Id)).ToList();", StudioSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The view rows and the container's images must be read TOGETHER, because a queued render is Pending in one and
    /// present in the other. Reading the rows alone after queueing made the panel conclude the artifact it had just
    /// queued was missing, and it told the operator the render would never arrive (reported 2026-10-11).
    /// </summary>
    [Fact]
    public void TheActionPath_ReadsTheImagesWithTheRows_NotTheRowsAlone()
    {
        Assert.Contains("private async Task LoadPanelStateAsync()", PanelSource, StringComparison.Ordinal);

        // The one loader builds the image map...
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(PanelSource, @"_imageStatuses\.Clear\(\);"));
        // ...and it is used by BOTH the refresh and the post-action path, which is the whole point: a second caller
        // that loaded only the rows is exactly the bug.
        var callers = System.Text.RegularExpressions.Regex.Matches(PanelSource, @"await LoadPanelStateAsync\(\);").Count;
        Assert.True(callers >= 2, $"LoadPanelStateAsync is called {callers} time(s); the action path and the refresh both need it.");

        // The missing-artifact check reads that map, so it is only ever as fresh as the last full load.
        Assert.Contains("!_imageStatuses.ContainsKey(view.OutputArtifactId!)", PanelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePanelPublishesOnlyWhenTheRowsActuallyChanged()
    {
        // The host re-renders the panel's parameters on every pass, so an unguarded raise from OnParametersSetAsync
        // would re-enter the panel and never settle. The signature is what makes the raise conditional.
        Assert.Contains("_publishedViewSignature", PanelSource, StringComparison.Ordinal);
        Assert.Contains("if (string.Equals(signature, _publishedViewSignature, StringComparison.Ordinal))", PanelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryAngleRowInTheNav_CarriesItsStatus()
    {
        // Each cell states its status, and the status comes from the mirrored rows.
        Assert.Contains("var view = BodyViewFor(angle);", StudioSource, StringComparison.Ordinal);
        Assert.Contains("BodyViewStatusDisplay.BadgeClass(view)", StudioSource, StringComparison.Ordinal);
        Assert.Contains("BodyViewStatusDisplay.Label(view)", StudioSource, StringComparison.Ordinal);

        // Each group counts its own progress, so a whole state's work is visible at a glance.
        Assert.Contains("var groupAccepted = groupViews.Count(BodyViewStatusDisplay.IsAccepted);", StudioSource, StringComparison.Ordinal);
        Assert.Contains("@groupAccepted / @groupViews.Count", StudioSource, StringComparison.Ordinal);

        // A slot with no row yet is still shown, because "never started" is exactly the thing the operator is looking
        // for — a missing row must not read as a missing control.
        Assert.Contains("FirstOrDefault(view => view.Key() == key)", StudioSource, StringComparison.Ordinal);
        Assert.Contains("CharacterIdentityBodyView? BodyViewFor(CharacterIdentityBodySlot slot)", StudioSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStatusColoursLiveInOnePlace_AndThePanelAndNavShareThem()
    {
        // The panel's own badge classes come from the shared table rather than a private switch. The status switch is
        // what must be gone — the panel legitimately names the same Bootstrap classes for its group-count badge, so
        // asserting the strings are absent would fail for a reason that has nothing to do with status.
        Assert.Contains("private static string StatusBadge(CharacterIdentityBodyView? view)", PanelSource, StringComparison.Ordinal);
        Assert.Contains("=> BodyViewStatusDisplay.BadgeClass(view);", PanelSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CharacterIdentityAngleStatus.Failed or CharacterIdentityAngleStatus.Blocked => \"text-bg-danger\"",
            PanelSource,
            StringComparison.Ordinal);
        Assert.Contains("public static string BadgeClass(CharacterIdentityAngleStatus? status)", DisplaySource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CharacterIdentityAngleStatus.Accepted, "text-bg-success", "Accepted", true)]
    [InlineData(CharacterIdentityAngleStatus.Complete, "text-bg-info", "Review", false)]
    [InlineData(CharacterIdentityAngleStatus.Pending, "text-bg-primary", "Rendering", false)]
    [InlineData(CharacterIdentityAngleStatus.Failed, "text-bg-danger", "Failed", false)]
    [InlineData(CharacterIdentityAngleStatus.Blocked, "text-bg-danger", "Blocked", false)]
    [InlineData(CharacterIdentityAngleStatus.NotStarted, "text-bg-secondary", "Not started", false)]
    public void AStatusHasOneColour_AndOneOperatorFacingWord(
        CharacterIdentityAngleStatus status, string expectedClass, string expectedLabel, bool accepted)
    {
        Assert.Equal(expectedClass, BodyViewStatusDisplay.BadgeClass(status));
        Assert.Equal(expectedLabel, BodyViewStatusDisplay.Label(status));
        Assert.Equal(accepted, !BodyViewStatusDisplay.NeedsWork(new CharacterIdentityBodyView { Status = status }));
    }

    [Fact]
    public void AViewWithNoRowYet_ReadsAsNotStarted_RatherThanAsNothing()
    {
        // Null is the state of a cell nobody has touched; it must be legible, not blank.
        Assert.Equal("text-bg-secondary", BodyViewStatusDisplay.BadgeClass((CharacterIdentityBodyView?)null));
        Assert.Equal("Not started", BodyViewStatusDisplay.Label((CharacterIdentityBodyView?)null));
        Assert.True(BodyViewStatusDisplay.NeedsWork(null));
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "DreamGenClone.sln")))
                return current.FullName;
        }
        throw new DirectoryNotFoundException("Could not find the DreamGenClone repository root.");
    }
}
