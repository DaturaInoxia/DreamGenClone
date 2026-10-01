using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The importer and the backfill: where a pose's metadata comes from, and the two promises the backfill makes —
/// it fills what is missing, and it never rewrites what is there.
///
/// The second promise is what makes it safe to run on a page load over a library whose poses an operator may have
/// edited, so it is pinned by a test rather than left to the SQL to be read correctly.
/// </summary>
public sealed class PoseLibraryImporterMetadataTests
{
    [Fact]
    public async Task AnImportedPoseCarriesTheMetadataItsPackDeclares()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WriteRaw(
            "pack.json",
            """
            {
              "name": "Test pack",
              "rating": "nsfw",
              "categories": { "NSFW_Kneeling": { "stance": "kneeling", "direction": "front", "camera": "eye-level" } }
            }
            """);

        var preset = await fixture.ImportPoseAsync("kneeling", "NSFW_Kneeling/512768/NSFW_Kneeling017.json");

        Assert.Equal(PoseContentRating.Nsfw, preset.ContentRating);
        Assert.Equal(PoseStance.Kneeling, preset.Stance);
        Assert.Equal(PoseFacingDirection.Front, preset.Direction);
        Assert.Equal(PoseCameraAngle.EyeLevel, preset.CameraAngle);

        // A prompt that describes THIS pose, not the generic "a fully clothed person" the panel used to start from.
        Assert.Contains("a naked woman", preset.MetadataPrompt, StringComparison.Ordinal);
        Assert.Contains("kneeling", preset.MetadataPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APackThatDeclaresNothingImportsWithNotDeclaredMetadataAndNoPrompt()
    {
        using var fixture = new PoseLibraryTestFixture();

        var preset = await fixture.ImportPoseAsync("standing", "NSFW_standing/512768/NSFW_standing028.json");

        Assert.Equal(PoseContentRating.Unrated, preset.ContentRating);
        Assert.Equal(PoseStance.Unknown, preset.Stance);
        Assert.Equal(PoseFacingDirection.Unknown, preset.Direction);
        Assert.Equal(string.Empty, preset.MetadataPrompt);

        // And the pack is NAMED as needing a declaration, once per category, rather than counted silently.
        var backfill = await fixture.Importer.EnsureMetadataAsync();

        Assert.Equal(0, backfill.Filled);
        Assert.Contains(
            backfill.NotDeclared,
            entry => entry.EndsWith("standing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheBackfillFillsMetadataForPosesThatWereImportedBeforeMetadataExisted()
    {
        using var fixture = new PoseLibraryTestFixture();
        var preset = await fixture.ImportPoseAsync("standing", "NSFW_standing/512768/NSFW_standing028.json");
        Assert.Equal(string.Empty, preset.MetadataPrompt);

        // The pack gains its declaration block, which is what happens when a pack is documented after it ships.
        fixture.WriteRaw(
            "pack.json",
            """
            {
              "name": "Test pack",
              "rating": "nsfw",
              "categories": { "NSFW_standing": { "stance": "standing", "direction": "front", "camera": "eye-level" } }
            }
            """);

        var first = await fixture.Importer.EnsureMetadataAsync();

        Assert.Equal(1, first.Filled);
        Assert.Empty(first.NotDeclared);

        var stored = await fixture.Repository.GetAsync(preset.Id);
        Assert.NotNull(stored);
        Assert.Equal(PoseStance.Standing, stored!.Stance);
        Assert.Equal(PoseContentRating.Nsfw, stored.ContentRating);
        Assert.Contains("standing", stored.MetadataPrompt, StringComparison.Ordinal);

        // Idempotent: a second run finds nothing to do, which is what makes it safe to call on every page load.
        var second = await fixture.Importer.EnsureMetadataAsync();
        Assert.Equal(0, second.Filled);
        Assert.Equal(1, second.Unchanged);
    }

    [Fact]
    public async Task TheBackfillNeverOverwritesMetadataThatIsAlreadyThere()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WriteRaw(
            "pack.json",
            """
            {
              "name": "Test pack",
              "rating": "sfw",
              "categories": { "standing": { "stance": "sitting", "direction": "front", "camera": "eye-level" } }
            }
            """);

        var preset = await fixture.ImportPoseAsync("standing", "standing/standing_01.json");
        Assert.Equal(PoseContentRating.Sfw, preset.ContentRating);

        // An operator edits the metadata (the ratings and prompts are theirs to change).
        preset.ContentRating = PoseContentRating.Nsfw;
        preset.MetadataPrompt = "an edited prompt that must survive";
        await fixture.Repository.UpsertAsync(preset);

        var backfill = await fixture.Importer.EnsureMetadataAsync();

        Assert.Equal(0, backfill.Filled);
        var stored = await fixture.Repository.GetAsync(preset.Id);
        Assert.NotNull(stored);
        Assert.Equal(PoseContentRating.Nsfw, stored!.ContentRating);
        Assert.Equal("an edited prompt that must survive", stored.MetadataPrompt);
    }

    [Fact]
    public async Task AReimportDoesNotOverwriteAnEditedPrompt()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WriteRaw(
            "pack.json",
            """
            {
              "name": "Test pack",
              "rating": "nsfw",
              "categories": { "standing": { "stance": "standing", "direction": "front", "camera": "eye-level" } }
            }
            """);

        var preset = await fixture.ImportPoseAsync("standing", "standing/standing_01.json");
        preset.MetadataPrompt = "an edited prompt that must survive a re-import";
        await fixture.Repository.UpsertAsync(preset);

        await fixture.Importer.ImportAsync();

        var stored = await fixture.Repository.GetAsync(preset.Id);
        Assert.NotNull(stored);
        Assert.Equal("an edited prompt that must survive a re-import", stored!.MetadataPrompt);

        // The rest of the row came from the same declaration and is unchanged, so the guard is about the metadata
        // columns only, not about skipping the write entirely.
        Assert.Equal(PoseStance.Standing, stored.Stance);
    }

    [Fact]
    public async Task ABadDeclarationStopsTheImportWithThePackAndTheOffendingText()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WriteRaw("pack.json", """{ "name": "Test pack", "rating": "explicit-ish" }""");
        fixture.WritePose("standing/standing_01.json");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Importer.ImportAsync());

        Assert.Contains("explicit-ish", error.Message, StringComparison.Ordinal);
        Assert.Contains("pack.json", error.Message, StringComparison.Ordinal);
    }
}
