using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The service is the one search path the UI and the picker share, so its filters and its refusals are pinned
/// here rather than left to the components.
/// </summary>
public sealed class PoseLibraryServiceTests
{
    [Fact]
    public async Task CreateLibrary_IsSearchableImmediately()
    {
        using var fixture = new PoseLibraryTestFixture();

        var library = await fixture.Service.CreateLibraryAsync("Handshakes", "Contact greetings");

        Assert.Equal("handshakes", library.Id);
        Assert.False(library.IsSystem);

        var libraries = await fixture.Service.ListLibrariesAsync();
        Assert.Contains(libraries, entry => entry.Id == "handshakes");

        await fixture.Repository.UpsertAsync(new PosePreset
        {
            Id = "handshake-1",
            Name = "Handshake",
            Category = "contact",
            LibraryId = library.Id,
            Keywords = "handshake grip",
            KeypointsJson = OpenPosePoseJson.Serialize(
                OpenPosePoseJson.Parse(OpenPosePoseJsonTests.PackDocument(), "search sample"))
        });

        var results = await fixture.Service.SearchAsync(new PoseLibraryQuery(LibraryId: library.Id));
        Assert.Single(results);
        Assert.Equal("Handshake", results[0].Name);

        var byKeyword = await fixture.Service.SearchAsync(new PoseLibraryQuery("grip"));
        Assert.Single(byKeyword);
    }

    [Fact]
    public async Task CreateLibrary_WithAnExistingName_IsRefusedInsteadOfReused()
    {
        using var fixture = new PoseLibraryTestFixture();
        await fixture.Service.CreateLibraryAsync("Handshakes", string.Empty);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CreateLibraryAsync("handshakes", string.Empty));

        Assert.Contains("already exists", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateLibrary_WithNoName_IsRefused()
    {
        using var fixture = new PoseLibraryTestFixture();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CreateLibraryAsync("   ", string.Empty));

        Assert.Contains("needs a name", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_ByKeyword_MatchesNameKeywordsAndCategory()
    {
        using var fixture = new PoseLibraryTestFixture();
        await fixture.ImportPoseAsync("kneeling", "NSFW_Kneeling/512768/NSFW_kneeling007.json", keywords: "crouch low");

        Assert.Single(await fixture.Service.SearchAsync(new PoseLibraryQuery("kneeling")));
        Assert.Single(await fixture.Service.SearchAsync(new PoseLibraryQuery("crouch")));
        Assert.Single(await fixture.Service.SearchAsync(new PoseLibraryQuery("007")));
        Assert.Empty(await fixture.Service.SearchAsync(new PoseLibraryQuery("standing")));
    }

    [Fact]
    public async Task ListCategories_ReportsWhatIsActuallyPresent()
    {
        using var fixture = new PoseLibraryTestFixture();
        await fixture.ImportPoseAsync("kneeling", "NSFW_Kneeling/512768/NSFW_kneeling007.json");
        await fixture.ImportPoseAsync("standing", "NSFW_standing/512768/NSFW_standing028.json");

        var categories = await fixture.Service.ListCategoriesAsync();

        Assert.Equal(new[] { "kneeling", "standing" }, categories.ToArray());
    }

    [Fact]
    public async Task ReadSkeleton_UnknownPreset_IsRefused()
    {
        using var fixture = new PoseLibraryTestFixture();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ReadSkeletonAsync("no-such-preset"));

        Assert.Contains("no-such-preset", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadSkeleton_MissingFile_NamesThePresetAndTheFix()
    {
        using var fixture = new PoseLibraryTestFixture();
        await fixture.Repository.UpsertAsync(new PosePreset
        {
            Id = "ghost",
            Name = "Ghost pose",
            Category = "standing",
            LibraryId = PoseLibraryIds.Authored,
            KeypointsJson = "[]",
            SkeletonPngPath = "library/ghost.png"
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ReadSkeletonAsync("ghost"));

        Assert.Contains("Ghost pose", error.Message, StringComparison.Ordinal);
        Assert.Contains("missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadSkeleton_ReturnsTheRenderedBytes_AndSkeletonUrlPointsAtThem()
    {
        using var fixture = new PoseLibraryTestFixture();
        await fixture.ImportPoseAsync("standing", "NSFW_standing/512768/NSFW_standing028.json");
        var preset = (await fixture.Repository.ListAsync()).Single();

        var bytes = await fixture.Service.ReadSkeletonAsync(preset.Id);

        Assert.True(bytes.Length > 0);

        // One skeleton folder per pack, so two packs may each hold a same-named file without colliding.
        var url = fixture.Service.SkeletonUrl(preset);
        Assert.NotNull(url);
        Assert.StartsWith("/pose-library/library/openpose-nsfw/", url!, StringComparison.Ordinal);
        Assert.EndsWith(".png", url!, StringComparison.Ordinal);
    }

    [Fact]
    public void Slug_ProducesAStableId()
    {
        Assert.Equal("my-new-library", PoseLibraryService.Slug("  My New / Library  "));
    }
}
