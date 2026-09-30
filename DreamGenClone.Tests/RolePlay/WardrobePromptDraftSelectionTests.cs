using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The wardrobe Prompt box takes what it shows from the ROWS, not from the request this browser session made. Reported
/// 2026-09-29 ("still not doing it... i should not have to refresh or click off tab and back"): a prompt had been compiled
/// and stored, and the box stayed empty because the panel only recognised a draft it had asked for itself. The cases
/// below pin that down.
/// </summary>
public sealed class WardrobePromptDraftSelectionTests
{
    [Fact]
    public void SelectPromptToShow_AddsADraftThisSessionNeverAskedFor_BecauseTheRowsAreTheTruth()
    {
        // This is the reported failure: the compile finished before the page was loaded (another tab, or a refresh), so
        // there is no session memory of it at all - and the box must still show the prompt.
        var items = new[] { Item("dress", Draft("d1", "dress", "A yellow sundress lies flat.")) };

        var selected = WardrobePromptDraftSelection.SelectPromptToShow(items, "dress");

        Assert.NotNull(selected);
        Assert.Equal("A yellow sundress lies flat.", selected!.Image.Prompt);
    }

    [Fact]
    public void SelectPromptToShow_IsStillBeingDrafted_ShowsNothingYet()
    {
        // Row exists, no compiler has written the prompt yet: there is nothing to show, and showing the operator's own
        // words back in the box would look like the compile had already happened.
        var items = new[] { Item("dress", Draft("d1", "dress", prompt: null, compiler: null)) };

        Assert.Null(WardrobePromptDraftSelection.SelectPromptToShow(items, "dress"));
    }

    [Fact]
    public void SelectPromptToShow_PrefersTheItemTheFormIsWorkingOn()
    {
        var items = new[]
        {
            Item("dress", Draft("d1", "dress", "A yellow sundress lies flat.", "compiler", minutesAgo: 30)),
            Item("shorts", Draft("s1", "shorts", "A pair of shorts lies flat.", "compiler", minutesAgo: 1))
        };

        var selected = WardrobePromptDraftSelection.SelectPromptToShow(items, "dress");

        Assert.Equal("d1", selected!.Image.Id);
    }

    [Fact]
    public void SelectPromptToShow_NoItemChosen_ShowsTheNewestDraft()
    {
        var items = new[]
        {
            Item("dress", Draft("d1", "dress", "A yellow sundress lies flat.", "compiler", minutesAgo: 30)),
            Item("shorts", Draft("s1", "shorts", "A pair of shorts lies flat.", "compiler", minutesAgo: 1))
        };

        var selected = WardrobePromptDraftSelection.SelectPromptToShow(items, itemId: null);

        Assert.Equal("s1", selected!.Image.Id);
    }

    [Fact]
    public void SelectPromptToShow_ChosenItemIsStillDrafting_ShowsTheNewestFinishedDraftInsteadOfNothing()
    {
        var items = new[]
        {
            Item("dress", Draft("d1", "dress", "A yellow sundress lies flat.", "compiler", minutesAgo: 30)),
            Item("shorts", Draft("s1", "shorts", prompt: null, compiler: null, minutesAgo: 0))
        };

        var selected = WardrobePromptDraftSelection.SelectPromptToShow(items, "shorts");

        Assert.Equal("d1", selected!.Image.Id);
    }

    [Fact]
    public void SelectPromptToShow_RenderedImagesAreNotDraftPrompts()
    {
        var items = new[]
        {
            Item("dress", new WardrobeItemImage(
                Row("r1", "dress", "A yellow sundress lies flat.", "compiler", minutesAgo: 1),
                Label: string.Empty,
                InUse: true,
                IsPromptDraft: false))
        };

        Assert.Null(WardrobePromptDraftSelection.SelectPromptToShow(items, "dress"));
    }

    [Fact]
    public void SelectPromptToShow_NoItems_IsNull() =>
        Assert.Null(WardrobePromptDraftSelection.SelectPromptToShow([], "dress"));

    private static WardrobeItem Item(string assetId, params WardrobeItemImage[] images) =>
        new(Asset(assetId), images);

    private static WardrobeItemImage Draft(
        string imageId,
        string assetId,
        string? prompt,
        string? compiler = "wardrobe-item-qwen-image-21-natural-language",
        int minutesAgo = 0) =>
        new(
            Row(imageId, assetId, prompt, compiler, minutesAgo),
            Label: string.Empty,
            InUse: false,
            IsPromptDraft: true);

    private static SceneAssetImage Row(string imageId, string assetId, string? prompt, string? compiler, int minutesAgo) =>
        new()
        {
            Id = imageId,
            AssetId = assetId,
            Prompt = prompt ?? string.Empty,
            PromptCompilerId = compiler,
            Status = SceneAssetStatus.Pending,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-minutesAgo)
        };

    private static SceneAsset Asset(string id) => new()
    {
        Id = id,
        Name = id,
        Type = SceneAssetType.Wardrobe,
        Status = SceneAssetStatus.Complete,
        CreatedUtc = DateTime.UtcNow
    };
}
