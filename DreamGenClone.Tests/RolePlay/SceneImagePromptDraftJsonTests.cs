using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The operator's UNCOMMITTED prompt-panel state — what they typed, whether or not they pressed Generate Prompt.
///
/// <para>
/// Reported live 2026-10-18: "I had entered a user intent prompt, but now I can't find it." A compiled prompt record is
/// not written until Generate Prompt is pressed, so before this text had nowhere to live and every reload reset the
/// panel to Scene with an empty intent box. These tests pin the stored shape, the default-is-null rule that keeps an
/// untouched panel from looking edited, and the fail-fast read: a draft that silently disappears is the bug.
/// </para>
/// </summary>
public sealed class SceneImagePromptDraftJsonTests
{
    [Fact]
    public void Serialize_SceneSourceAndNoIntent_StoresNothing()
    {
        // The default panel IS Scene with no text: storing that would mark every group as having a draft.
        Assert.Null(SceneImagePromptDraftJson.Serialize(SceneImagePromptSource.Scene, null));
        Assert.Null(SceneImagePromptDraftJson.Serialize(SceneImagePromptSource.Scene, "   "));
    }

    [Fact]
    public void Serialize_ThenRead_RoundTripsSourceAndIntent()
    {
        const string intent = "She stands at the clothesline in the white tank top from <image2>.";
        var json = SceneImagePromptDraftJson.Serialize(SceneImagePromptSource.UserIntent, intent);

        Assert.NotNull(json);
        var draft = SceneImagePromptDraftJson.Read(json, "group-1");

        Assert.NotNull(draft);
        Assert.Equal(SceneImagePromptSource.UserIntent, draft!.Source);
        Assert.Equal(intent, draft.UserIntent);
    }

    [Fact]
    public void Serialize_SceneSourceWithIntent_KeepsTheTextSoSwitchingBackDoesNotLoseIt()
    {
        var json = SceneImagePromptDraftJson.Serialize(SceneImagePromptSource.Scene, "typed but not compiled");
        var draft = SceneImagePromptDraftJson.Read(json, "group-1");

        Assert.Equal(SceneImagePromptSource.Scene, draft!.Source);
        Assert.Equal("typed but not compiled", draft.UserIntent);
    }

    [Fact]
    public void Read_NoStoredDraft_IsNullRatherThanAnError()
    {
        Assert.Null(SceneImagePromptDraftJson.Read(null, "group-1"));
        Assert.Null(SceneImagePromptDraftJson.Read(string.Empty, "group-1"));
        Assert.Null(SceneImagePromptDraftJson.Read("   ", "group-1"));
    }

    [Fact]
    public void Read_UnreadableDraft_FailsFastNamingTheGroup()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => SceneImagePromptDraftJson.Read("{not json", "group-42"));

        Assert.Contains("group-42", error.Message, StringComparison.Ordinal);
        Assert.Contains("unreadable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_UnknownSource_FailsFastRatherThanGuessing()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => SceneImagePromptDraftJson.Read("""{"source":99,"userIntent":"x"}""", "group-42"));

        Assert.Contains("group-42", error.Message, StringComparison.Ordinal);
        Assert.Contains("not a known source", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Prompt Input inspector edits are part of the panel. They used to live only in the page's memory, so a reload
    /// threw them away and the next Generate sent no overrides at all (reported 2026-10-22, "I think I lost the
    /// changes").
    /// </summary>
    [Fact]
    public void Serialize_ThenRead_RoundTripsTheInspectorOverrides()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields =
            [
                new ScenePromptFieldOverride
                {
                    ElementKey = "frozenState.visualDescription",
                    Value = "Dean stands clothed above her, his pants undone with the fly wide open."
                },
                new ScenePromptFieldOverride { ElementKey = "scene.mood", Removed = true }
            ],
            RemovedCharacters = ["carter"]
        };

        var json = SceneImagePromptDraftJson.Serialize(SceneImagePromptSource.Scene, null, overrides);
        var draft = SceneImagePromptDraftJson.Read(json, "group-1");

        Assert.NotNull(draft);
        var read = draft!.Overrides;
        Assert.NotNull(read);
        Assert.Equal(2, read!.Fields.Count);
        Assert.Equal("frozenState.visualDescription", read.Fields[0].ElementKey);
        Assert.Equal("Dean stands clothed above her, his pants undone with the fly wide open.", read.Fields[0].Value);
        Assert.False(read.Fields[0].Removed);
        Assert.True(read.Fields[1].Removed);
        Assert.Equal(["carter"], read.RemovedCharacters);
    }

    /// <summary>Inspector edits alone are enough to make a stored draft: the panel is not "default" any more.</summary>
    [Fact]
    public void Serialize_SceneSourceWithOnlyOverrides_StillStoresTheDraft()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "scene.mood", Value = "quiet" }]
        };

        var json = SceneImagePromptDraftJson.Serialize(SceneImagePromptSource.Scene, null, overrides);

        Assert.NotNull(json);
        Assert.Equal("quiet", SceneImagePromptDraftJson.Read(json, "group-1")!.Overrides!.Fields[0].Value);
    }

    /// <summary>A draft written before the overrides existed still reads, with nothing to apply.</summary>
    [Fact]
    public void Read_DraftWithoutOverrides_IsReadableAndCarriesNone()
    {
        var draft = SceneImagePromptDraftJson.Read(
            """{"source":1,"userIntent":"as written"}""", "group-1");

        Assert.Equal(SceneImagePromptSource.UserIntent, draft!.Source);
        Assert.Equal("as written", draft.UserIntent);
        Assert.Null(draft.Overrides);
    }
}
