using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The review deck's metadata rail is ONE component for every route, and the studio route shows what the asset route
/// shows about the same kind of picture.
///
/// <para>
/// These are source contracts rather than rendered assertions, matching the other UI contract tests here: what is pinned
/// is the SHAPE the deck must keep — one rail, the tag store named per route, the generation inputs read from the
/// image's own row, and an approve control that asks the DOMAIN gate rather than re-deriving the rule inline (which is
/// how the deck and the studio came to disagree about the same attempt).
/// </para>
/// </summary>
public sealed class ReviewDeckMetadataParityContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string DeckSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "ReviewDeck.razor"));
    private static readonly string RailSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Shared", "ImageMetadataRail.razor"));
    private static readonly string ProductionServiceSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Application", "RolePlay", "SceneImageProductionService.cs"));

    [Fact]
    public void Deck_RendersOneSharedRailForEveryRoute()
    {
        // ONE rail, rendered once — not a branch per route with a shorter field list in each: the studio-only rows
        // (Stage, Execution) now come from the shared component's union field list.
        Assert.Contains("<ImageMetadataRail Item=\"Current\"", DeckSource, StringComparison.Ordinal);
        Assert.DoesNotContain("<dt class=\"col-sm-4\">Stage</dt>", DeckSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Deck_NamesTheStoreEachRoutesImageActuallyLivesIn()
    {
        // A rendered production frame's tags live in the rendered store, and its inputs are read from its own row.
        // The id is only meaningful inside the store that issued it, so the route picks the store, never the rail.
        Assert.Contains("ImageTagTarget.Rendered", DeckSource, StringComparison.Ordinal);
        Assert.Contains("ImageGenerationInputDetails.FromSceneImage(rendered)", DeckSource, StringComparison.Ordinal);
        Assert.Contains("ImageTagTarget.AssetLibrary", DeckSource, StringComparison.Ordinal);
        Assert.Contains("ImageGenerationInputDetails.FromAssetImage(asset)", DeckSource, StringComparison.Ordinal);

        // The asset-backed routes — the asset batch and the whole-dataset scope, whose items ARE asset images — share
        // one branch, so a cell attempt cannot show a different rail from the batch it belongs to.
        Assert.Contains("if (IsImageRoute && await AssetService.GetImageAsync(Current.Id) is { } asset)",
            DeckSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Rail_CarriesTheTagEditorAndTheGenerationInputsForWhicheverStoreIsNamed()
    {
        Assert.Contains("<ImageTags ImageId=\"@Item.Id\" Target=\"@tagTarget\"", RailSource, StringComparison.Ordinal);
        Assert.Contains("<GenerationDetails Details=\"Details\" />", RailSource, StringComparison.Ordinal);

        // The tag store is the CALLER's to name: a route whose images have no tag store hands null and the rail offers
        // no editor, rather than an editor that would refuse every write.
        Assert.Contains("[Parameter] public ImageTagTarget? TagTarget { get; set; }", RailSource, StringComparison.Ordinal);
        Assert.Contains("@if (TagTarget is { } tagTarget)", RailSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The approve control asks the domain gate. It used to carry its own inline condition, and the deck therefore
    /// refused frames the studio's own control offered — the same attempt, two verdicts.
    /// </summary>
    [Fact]
    public void Deck_AsksTheDomainGateForApprovalAndOffersTheRevokeTheStudioRouteLacked()
    {
        Assert.Contains("disabled=\"@(_busy || Current.ApprovalBlockedReason is not null)\"", DeckSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Current.ExecutionStatus != SceneImageStatus.Complete || string.IsNullOrWhiteSpace(Current.Sha256)",
            DeckSource, StringComparison.Ordinal);

        // Withdrawing an approval is the asset route's own verdict applied to a production frame, through the service
        // that owns the write.
        Assert.Contains("await ProductionService.RevokeApprovalAsync(BatchId, \"Review Deck\", null);", DeckSource,
            StringComparison.Ordinal);
        Assert.Contains("@if (Current.IsApproved)", DeckSource, StringComparison.Ordinal);

        // And the service refuses a withdrawal that has nothing to withdraw, from the store that owns the fact.
        Assert.Contains("RevokeCurrentApprovalAsync(", ProductionServiceSource, StringComparison.Ordinal);
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

/// <summary>
/// The deck's projection carries the approval verdict the service will apply, so a surface that already holds a
/// projection does not have to fetch the group to ask the same question.
/// </summary>
public sealed class ReviewDeckMapperTests
{
    [Fact]
    public void MapStudio_CarriesTheDomainGatesOwnVerdict()
    {
        var group = new SceneImageProductionGroup { Id = "group-1", SessionId = "session-1" };
        var complete = Complete("attempt-1", "group-1");

        var approvable = ReviewDeckMapper.MapStudio(complete, isApproved: false);
        Assert.Null(approvable.ApprovalBlockedReason);
        Assert.False(approvable.IsApproved);

        var approved = ReviewDeckMapper.MapStudio(complete, isApproved: true);
        Assert.True(approved.IsApproved);
        Assert.Equal("Approved", approved.Status);

        complete.Sha256 = null;
        var blocked = ReviewDeckMapper.MapStudio(complete, isApproved: false);

        // The reason shown here IS the reason the service throws: one gate, asked once, on the same row.
        Assert.Equal(
            SceneImageFrameApprovalGate.BlockerFor(group, complete)!.Message,
            blocked.ApprovalBlockedReason);
    }

    [Fact]
    public void MapStudio_KeepsTheDispositionWhereTheDeckReadsIt()
    {
        var image = Complete("attempt-2", "group-1");
        image.Disposition = SceneImageAttemptDisposition.Shortlisted;

        var mapped = ReviewDeckMapper.MapStudio(image, isApproved: false);

        Assert.Equal(SceneImageAttemptDisposition.Shortlisted, mapped.Disposition);
        Assert.Equal("Shortlisted", mapped.Status);
        Assert.Equal(SceneImageStatus.Complete, mapped.ExecutionStatus);
    }

    private static SceneImageRecord Complete(string id, string groupId) => new()
    {
        Id = id,
        SessionId = "session-1",
        InteractionId = "interaction-1",
        ProductionGroupId = groupId,
        PromptSnapshot = "a woman on a bed",
        Status = SceneImageStatus.Complete,
        FileRelativePath = $"session-1/{id}.png",
        Sha256 = "ABC123"
    };
}
