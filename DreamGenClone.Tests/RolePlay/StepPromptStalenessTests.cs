using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Staleness is the "mark it, do not silently fix it" rule (D5): changing a binding must not overwrite a prompt the
/// operator has edited, and it must not leave them believing the draft still matches.
/// </summary>
public sealed class StepPromptStalenessTests
{
    private static ReferenceApplicationSelection Binding(
        int ordinal,
        string kind = "Location",
        string? actorKey = null,
        string imageId = "image-1",
        string sha = "SHA") => new()
    {
        ElementKey = kind,
        Kind = kind,
        ActorKey = actorKey,
        SemanticRole = "role",
        Strategy = "NativeMultiReference",
        Ordinal = ordinal,
        SceneAssetId = "asset-1",
        SceneAssetImageId = imageId,
        SceneAssetVersion = 1,
        SceneAssetSha256 = sha
    };

    [Fact]
    public void SignatureFor_NoBindings_IsTheNoneSignature()
    {
        Assert.Equal(StepPromptStaleness.NoBindings, StepPromptStaleness.SignatureFor(null));
        Assert.Equal(StepPromptStaleness.NoBindings, StepPromptStaleness.SignatureFor([]));
    }

    [Fact]
    public void SignatureFor_SameBindings_IsStable()
    {
        var first = StepPromptStaleness.SignatureFor([Binding(1), Binding(2, "Face", "p-becky")]);
        var second = StepPromptStaleness.SignatureFor([Binding(1), Binding(2, "Face", "p-becky")]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void SignatureFor_AddedBinding_ChangesTheSignature()
    {
        var before = StepPromptStaleness.SignatureFor([Binding(1)]);
        var after = StepPromptStaleness.SignatureFor([Binding(1), Binding(2, "Face", "p-becky")]);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// Order is request data - the first reference anchors the frame - so swapping two references genuinely changes
    /// the render and must be reported as a change rather than passing as "the same images are bound".
    /// </summary>
    [Fact]
    public void SignatureFor_ReorderedBindings_ChangesTheSignature()
    {
        var before = StepPromptStaleness.SignatureFor([Binding(1, "Face", "p-becky"), Binding(2, "Location")]);
        var after = StepPromptStaleness.SignatureFor([Binding(1, "Location"), Binding(2, "Face", "p-becky")]);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void SignatureFor_DifferentImageForTheSameSlot_ChangesTheSignature()
    {
        var before = StepPromptStaleness.SignatureFor([Binding(1, imageId: "image-1")]);
        var after = StepPromptStaleness.SignatureFor([Binding(1, imageId: "image-2")]);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void SignatureFor_DifferentContentBehindTheSameImageId_ChangesTheSignature()
    {
        // The sha is what actually pins the bytes, so a re-exported image under the same id must still register.
        var before = StepPromptStaleness.SignatureFor([Binding(1, sha: "SHA-1")]);
        var after = StepPromptStaleness.SignatureFor([Binding(1, sha: "SHA-2")]);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void IsStale_UnknownSignature_IsNotStale()
    {
        Assert.False(StepPromptStaleness.IsStale(null, [Binding(1)]));
        Assert.False(StepPromptStaleness.IsStale("   ", [Binding(1)]));
    }

    [Fact]
    public void IsStale_MatchingSignature_IsNotStale()
    {
        var bindings = new List<ReferenceApplicationSelection> { Binding(1) };
        var signature = StepPromptStaleness.SignatureFor(bindings);

        Assert.False(StepPromptStaleness.IsStale(signature, bindings));
    }

    [Fact]
    public void IsStale_ChangedBindings_IsStale()
    {
        var signature = StepPromptStaleness.SignatureFor([Binding(1)]);

        Assert.True(StepPromptStaleness.IsStale(signature, [Binding(1), Binding(2, "Face", "p-becky")]));
        Assert.True(StepPromptStaleness.IsStale(signature, []));
    }

    [Fact]
    public void DescribeChanges_IdenticalBindings_SaysNothing()
    {
        var bindings = new List<ReferenceApplicationSelection> { Binding(1) };

        Assert.Empty(StepPromptStaleness.DescribeChanges(bindings, bindings));
    }

    [Fact]
    public void DescribeChanges_AddedAndRemoved_NamesBoth()
    {
        var changes = StepPromptStaleness.DescribeChanges(
            [Binding(1, "Location")],
            [Binding(1, "Location"), Binding(2, "Face", "p-becky")]);

        var change = Assert.Single(changes);
        Assert.StartsWith("added", change, StringComparison.Ordinal);
        Assert.Contains("Face", change, StringComparison.Ordinal);
        Assert.Contains("p-becky", change, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeChanges_Removed_NamesIt()
    {
        var changes = StepPromptStaleness.DescribeChanges(
            [Binding(1, "Location"), Binding(2, "Face", "p-becky")],
            [Binding(1, "Location")]);

        var change = Assert.Single(changes);
        Assert.StartsWith("removed", change, StringComparison.Ordinal);
        Assert.Contains("Face", change, StringComparison.Ordinal);
    }
}
