using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The pose metadata editor's two write paths. Each test here pins a promise the editor makes to the operator rather
/// than an implementation detail:
///
///   * SAVE writes the pose and metadata over the row they came from — including a row that came out of a pack.
///   * SAVE AS adds a pose and leaves the original exactly as it was.
///   * an operator's edit is never taken away, not by a re-import and not by the backfill — even when the edit set
///     ONE field and left the rest "not declared", which is the state the values alone cannot express.
///
/// That last one is the reason the row carries an operator-edit marker at all, so it is pinned by a test instead of
/// being left to the fill-only guard to be read correctly.
/// </summary>
public sealed class PoseMetadataEditorTests
{
    /// <summary>A pack that declares its category fully, so the backfill has something to write.</summary>
    private const string DeclaringPack = """
        {
          "name": "Test pack",
          "rating": "nsfw",
          "categories": { "NSFW_Kneeling": { "stance": "kneeling", "direction": "front", "camera": "eye-level" } }
        }
        """;

    private const string SilentPack = """{ "name": "Test pack", "source": "test" }""";

    private const string PosePath = "NSFW_Kneeling/512768/NSFW_Kneeling017.json";

    [Fact]
    public async Task SavingMetadataOnAPackPoseWritesOverThatRow()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WriteRaw("pack.json", DeclaringPack);
        var pose = await fixture.ImportPoseAsync("kneeling", PosePath);
        var authored = await fixture.Service.EnsureAuthoredLibraryAsync();

        var overwritten = await fixture.Service.OverwritePoseAsync(
            pose.Id,
            Request(pose, authored.Id, PoseCameraAngle.FromAbove));

        // The SAME row: saving over a pose does not add one.
        var stored = await fixture.Repository.ListAsync();
        var row = Assert.Single(stored);
        Assert.Equal(pose.Id, overwritten.Id);
        Assert.Equal(PoseCameraAngle.FromAbove, row.CameraAngle);

        // The row keeps what belongs to the ROW rather than to the edit — the pack it came from and the keywords the
        // library's search matches on.
        Assert.Equal(pose.LibraryId, row.LibraryId);
        Assert.Equal(pose.Keywords, row.Keywords);

        // And it is now the operator's: from here the importer and the backfill leave it alone.
        Assert.True(row.MetadataOperatorEdited);
    }

    [Fact]
    public async Task SaveAsAddsAPoseAndLeavesTheOriginalAlone()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WriteRaw("pack.json", DeclaringPack);
        var pose = await fixture.ImportPoseAsync("kneeling", PosePath);
        var authored = await fixture.Service.EnsureAuthoredLibraryAsync();

        var created = await fixture.Service.SaveAuthoredPoseAsync(
            Request(pose, authored.Id, PoseCameraAngle.FromAbove) with { Name = "Kneeling, from above" });

        var stored = await fixture.Repository.ListAsync();
        Assert.Equal(2, stored.Count);

        // The new pose carries the edit, in the library the operator chose.
        var added = Assert.Single(stored, entry => entry.Id == created.Id);
        Assert.Equal("Kneeling, from above", added.Name);
        Assert.Equal(PoseCameraAngle.FromAbove, added.CameraAngle);
        Assert.Equal(authored.Id, added.LibraryId);
        Assert.True(added.MetadataOperatorEdited);

        // The pose it was copied from is untouched — still the pack's row, still the pack's metadata.
        var original = Assert.Single(stored, entry => entry.Id == pose.Id);
        Assert.Equal(pose.CameraAngle, original.CameraAngle);
        Assert.Equal(pose.MetadataPrompt, original.MetadataPrompt);
        Assert.False(original.MetadataOperatorEdited);
    }

    [Fact]
    public async Task AnOperatorEditSurvivesAReimportAndABackfillWhenItSetOnlyOneField()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WriteRaw("pack.json", SilentPack);
        var pose = await fixture.ImportPoseAsync("kneeling", PosePath);
        var authored = await fixture.Service.EnsureAuthoredLibraryAsync();

        // The case the marker exists for: the operator sets ONLY the camera angle, which is the one field the packs
        // cannot declare. Stance, direction and rating are deliberately left "not declared", so the row still LOOKS
        // untouched by every value the fill-only guard reads.
        await fixture.Service.OverwritePoseAsync(
            pose.Id,
            new AuthoredPoseRequest(
                pose.Name,
                pose.Category,
                Keywords: null,
                View: new PoseView(),
                LibraryId: authored.Id,
                Keypoints: OpenPosePoseJson.Parse(pose.KeypointsJson, pose.Name),
                Metadata: new PoseMetadataEdit(
                    PoseStance.Unknown,
                    PoseFacingDirection.Unknown,
                    PoseCameraAngle.FromAbove,
                    PoseContentRating.Unrated,
                    string.Empty)));

        // The pack is then documented, and re-imported — which is exactly what would have filled the row.
        fixture.WriteRaw("pack.json", DeclaringPack);
        await fixture.Importer.ImportAsync();
        var backfill = await fixture.Importer.EnsureMetadataAsync();

        var row = await fixture.Repository.GetAsync(pose.Id);
        Assert.NotNull(row);

        // The operator's camera is still there...
        Assert.Equal(PoseCameraAngle.FromAbove, row!.CameraAngle);
        Assert.True(row.MetadataOperatorEdited);

        // ...and nothing was filled in around it, because the row is the operator's now.
        Assert.Equal(PoseStance.Unknown, row.Stance);
        Assert.Equal(PoseContentRating.Unrated, row.ContentRating);
        Assert.Equal(string.Empty, row.MetadataPrompt);
        Assert.Equal(0, backfill.Filled);
    }

    [Fact]
    public async Task OverwritingAPoseThatIsNotThereFailsLoudly()
    {
        using var fixture = new PoseLibraryTestFixture();
        var authored = await fixture.Service.EnsureAuthoredLibraryAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.OverwritePoseAsync(
                "no-such-pose",
                new AuthoredPoseRequest(
                    "Anything",
                    "standing",
                    Keywords: null,
                    View: new PoseView(),
                    LibraryId: authored.Id)));

        // Named and actionable: the operator's next move is Save As, which the message says.
        Assert.Contains("nothing to overwrite", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Save it as a new pose", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavingMetadataClearsTheReviewFlagItHasAnswered()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WriteRaw("pack.json", DeclaringPack);

        // The fixture's keypoints are a flat diagonal, so the pack's upright "kneeling" declaration disagrees with
        // them and the pose is flagged — which is the state the editor is asked to resolve.
        var pose = await fixture.ImportPoseAsync("kneeling", PosePath);
        Assert.True(pose.MetadataNeedsReview);

        var authored = await fixture.Service.EnsureAuthoredLibraryAsync();
        await fixture.Service.OverwritePoseAsync(pose.Id, Request(pose, authored.Id, PoseCameraAngle.EyeLevel));

        var row = await fixture.Repository.GetAsync(pose.Id);
        Assert.NotNull(row);

        // Cleared rather than re-raised: the flag asked for a decision, and the operator has now made one.
        Assert.False(row!.MetadataNeedsReview);
        Assert.Equal(string.Empty, row.MetadataReviewNote);
    }

    /// <summary>
    /// A save request for a loaded pose, carrying the pose's OWN keypoints plus the operator's metadata — which is what
    /// the panel sends when a library pose is open and only its metadata was touched.
    /// </summary>
    private static AuthoredPoseRequest Request(PosePreset pose, string libraryId, PoseCameraAngle camera) =>
        new(
            pose.Name,
            pose.Category,
            Keywords: null,
            View: new PoseView(),
            LibraryId: libraryId,
            Keypoints: OpenPosePoseJson.Parse(pose.KeypointsJson, pose.Name),
            Metadata: new PoseMetadataEdit(
                PoseStance.Kneeling,
                PoseFacingDirection.Front,
                camera,
                PoseContentRating.Nsfw,
                "A full-body photograph of a naked woman kneeling facing the camera."));
}
