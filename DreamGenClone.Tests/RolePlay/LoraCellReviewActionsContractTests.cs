using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The LoRA cell's attempt list uses the SAME review actions as the face and body workflows (2026-09-27).
///
/// It showed the attempts and let you discard the selected one, and nothing else: blessing an image meant opening the
/// review deck, which is a different surface for a different job. The actions now come from the shared
/// <c>CandidateGrid</c>, so the grid is where they are added and every host that supplies the callbacks gets them.
/// </summary>
public sealed class LoraCellReviewActionsContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string Grid => Read("DreamGenClone.Web", "Components", "Shared", "CandidateGrid.razor");

    private static string Workspace => Read("DreamGenClone.Web", "Components", "Editing", "LoraDatasetWorkspace.razor");

    /// <summary>
    /// The grid offers the review deck's own verdicts, and offers them ONLY when the host asks: a read-only grid passes
    /// no callbacks and renders exactly as it did.
    /// </summary>
    [Fact]
    public void TheGridOffersTheStandardActions_OnlyWhenTheHostAsksForThem()
    {
        Assert.Contains("DecisionChanged.HasDelegate || DeleteRequested.HasDelegate", Grid, StringComparison.Ordinal);
        Assert.Contains("EventCallback<CandidateGridDecision> DecisionChanged", Grid, StringComparison.Ordinal);
        Assert.Contains("EventCallback<string> DeleteRequested", Grid, StringComparison.Ordinal);
        Assert.Contains("\"Accepted\"", Grid, StringComparison.Ordinal);
        Assert.Contains("\"Shortlisted\"", Grid, StringComparison.Ordinal);
        Assert.Contains("\"Rejected\"", Grid, StringComparison.Ordinal);
    }

    /// <summary>
    /// The actions are real buttons and the tile is their sibling, not their parent: a button inside a button is
    /// invalid markup and swallows the inner click, which is how a review action silently does nothing.
    /// </summary>
    [Fact]
    public void TheActionsAreNotNestedInsideTheTileButton()
    {
        var actionIndex = Grid.IndexOf("DecisionChanged.HasDelegate || DeleteRequested.HasDelegate", StringComparison.Ordinal);
        Assert.True(actionIndex > 0);

        // The action block comes AFTER the tile's closing </button>.
        var tileClose = Grid.IndexOf("</button>", StringComparison.Ordinal);
        Assert.True(tileClose > 0 && tileClose < actionIndex, "The review actions must sit outside the tile's button.");
    }

    /// <summary>
    /// The cell wires the standard actions, the decision it records is the field the grid shows, and the deck it links
    /// to is the SCENE-ASSET route — the bare <c>/review-deck/{batchId}</c> route reads the older produced-images
    /// table, so a cell's batch opened there shows nothing.
    /// </summary>
    [Fact]
    public void TheCellWiresTheActionsAndLinksTheAssetReviewDeck()
    {
        Assert.Contains("DecisionChanged=\"OnAttemptDecisionAsync\"", Workspace, StringComparison.Ordinal);
        Assert.Contains("DeleteRequested=\"OnAttemptDeleteAsync\"", Workspace, StringComparison.Ordinal);
        Assert.Contains("Busy=\"_cellBusy\"", Workspace, StringComparison.Ordinal);
        Assert.Contains("attempt.CandidateDecision?.ToString()", Workspace, StringComparison.Ordinal);
        Assert.Contains("/review-deck/asset/", Workspace, StringComparison.Ordinal);
    }

    /// <summary>The verdict a list action records is the enum the render path writes, not a second vocabulary.</summary>
    [Fact]
    public void TheDecisionTypeIsTheSharedOne()
    {
        var service = Read("DreamGenClone.Web", "Application", "RolePlay", "CharacterLoraCellService.cs");

        Assert.Contains("SetImageCandidateDecisionAsync", service, StringComparison.Ordinal);
        Assert.Contains("SceneAssetCandidateDecision decision", service, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root (holding DreamGenClone.sln) was not found.");
    }
}
