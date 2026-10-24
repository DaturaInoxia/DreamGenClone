namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-157 T1.6 — the /profiles Image Staging tab.
///
/// <para>
/// The placement ladder, the depths and the camera are CONFIGURATION, so there has to be a surface that writes them;
/// a rule only editable with a SQL client is a code default wearing a table. These assertions are about wiring, which
/// a unit test cannot reach: the tab has to be hosted on the profiles page, and the panel has to go through the store
/// and its one parser rather than growing a second interpretation of the same JSON.
/// </para>
/// </summary>
public sealed class ImageStagingProfilePanelContractTests
{
    [Fact]
    public void TheProfilesPageHostsTheImageStagingTab()
    {
        var page = ReadWebFile("Components", "Pages", "ThemeProfiles.razor");

        Assert.Contains("SelectTab(\"image-staging\")", page, StringComparison.Ordinal);
        Assert.Contains("GetTabClass(\"image-staging\")", page, StringComparison.Ordinal);
        Assert.Contains("<ImageStagingProfilePanel />", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePanelWritesThroughTheStoreAndTheSingleParser()
    {
        var panel = ReadWebFile("Components", "Shared", "ImageStagingProfilePanel.razor");

        Assert.Contains("IImageStagingProfileRepository", panel, StringComparison.Ordinal);
        Assert.Contains("UpsertAsync(profile)", panel, StringComparison.Ordinal);
        // One owner for reading the ladder and the keyword map: longhand JSON parsing here would be a second rule.
        Assert.Contains("ImageStagingProfileValidation.ReadPlacementLadder", panel, StringComparison.Ordinal);
        Assert.Contains("ImageStagingProfileValidation.ReadCameraKeywordMap", panel, StringComparison.Ordinal);
        // The version a render records has to move when the profile is edited, or provenance goes stale.
        Assert.Contains("profile.Version += 1", panel, StringComparison.Ordinal);
    }

    private static string ReadWebFile(params string[] relativeParts)
    {
        var path = Path.Combine([FindRepositoryRoot(), "DreamGenClone.Web", .. relativeParts]);
        return File.ReadAllText(path);
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
