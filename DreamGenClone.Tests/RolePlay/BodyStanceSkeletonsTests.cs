using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The stance → skeleton map and the reader that serves it (B-122).
///
/// Two things are held here. First, the map is COMPLETE for the stance enum: the brief offers exactly the stances
/// measured to hold under OpenPoseXL2, and a stance that could be picked but has no skeleton would fail at render
/// time with the operator already committed. Second, the COMMITTED skeletons actually load — the conditioning input
/// has to be reproducible from the repository, not from whoever rendered it last.
/// </summary>
public sealed class BodyStanceSkeletonsTests
{
    [Theory]
    [InlineData(BodyReferenceStance.Standing)]
    [InlineData(BodyReferenceStance.Squatting)]
    [InlineData(BodyReferenceStance.Kneeling)]
    public void EveryStanceTheBriefOffers_HasAVerifiedSkeleton(BodyReferenceStance stance)
    {
        var skeleton = BodyStanceSkeletons.Require(stance);

        Assert.EndsWith(".png", skeleton.FileName, StringComparison.Ordinal);
        // Provenance is recorded: which source pose this was rendered from.
        Assert.Contains(".json", skeleton.VerifiedOn, StringComparison.Ordinal);
    }

    /// <summary>
    /// The map and the enum are the same set. A stance in one but not the other is either an offer that cannot be
    /// honoured or a skeleton nothing can ask for.
    /// </summary>
    [Fact]
    public void TheAvailableSet_IsExactlyTheStanceEnum()
    {
        Assert.Equal(
            Enum.GetValues<BodyReferenceStance>().OrderBy(value => value),
            BodyStanceSkeletons.Available.OrderBy(value => value));
    }

    /// <summary>A stance outside the verified set is refused rather than mapped to something else.</summary>
    [Fact]
    public void AnUnverifiedStance_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => BodyStanceSkeletons.Require((BodyReferenceStance)99));

        Assert.Contains("no verified OpenPose skeleton", error.Message, StringComparison.Ordinal);
        Assert.Contains("Standing", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The kneeling caveat is recorded on the map itself, not only in a report: an operator about to condition on
    /// raised arms should be able to see that before generating.
    /// </summary>
    [Fact]
    public void TheKneelingLimitation_IsRecorded()
    {
        var skeleton = BodyStanceSkeletons.Require(BodyReferenceStance.Kneeling);

        Assert.NotNull(skeleton.KnownLimitation);
        Assert.Contains("overhead", skeleton.KnownLimitation!, StringComparison.OrdinalIgnoreCase);
    }

    // ── The reader ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The committed skeletons load, as real PNG bytes. This is what proves the conditioning input ships with the
    /// repository — a unit test on the map alone would pass even if no file existed.
    /// </summary>
    [Theory]
    [InlineData(BodyReferenceStance.Standing)]
    [InlineData(BodyReferenceStance.Squatting)]
    [InlineData(BodyReferenceStance.Kneeling)]
    public async Task TheCommittedSkeleton_LoadsAsPngBytes(BodyReferenceStance stance)
    {
        var provider = new StancePoseSkeletonProvider(WebRootAtRepoPoseLibrary());

        var bytes = await provider.ReadAsync(stance);

        Assert.True(bytes.Length > 1000, $"{provider.FileNameFor(stance)} was only {bytes.Length} bytes.");
        // PNG magic — the ControlNet loader expects an image, not a placeholder.
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4).ToArray());
    }

    /// <summary>
    /// A missing skeleton fails loudly and names the tool that regenerates it. Rendering unconditioned instead would
    /// produce an image that looks like every other candidate and quietly is not the pose that was asked for.
    /// </summary>
    [Fact]
    public async Task AMissingSkeleton_IsRefused_NotSilentlySkipped()
    {
        var empty = Path.Combine(Path.GetTempPath(), $"pose-library-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        try
        {
            var provider = new StancePoseSkeletonProvider(StubEnvironment(empty));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.ReadAsync(BodyReferenceStance.Standing));

            Assert.Contains("is missing", error.Message, StringComparison.Ordinal);
            Assert.Contains("render-single-pose.py", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    /// <summary>A web root that is not configured at all is a refusal too, not an imaged-less render.</summary>
    [Fact]
    public async Task AMissingWebRoot_IsRefused()
    {
        var provider = new StancePoseSkeletonProvider(StubEnvironment(webRootPath: null));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ReadAsync(BodyReferenceStance.Standing));

        Assert.Contains("web root", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A stand-in web root pointing at the repository's real <c>pose-library</c>, found by walking up from the test
    /// assembly to the solution file — so the test proves the COMMITTED assets, wherever the build put the output.
    /// </summary>
    private static IWebHostEnvironment WebRootAtRepoPoseLibrary()
        => StubEnvironment(Path.Combine(FindRepositoryRoot(), "DreamGenClone.Web", "wwwroot"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"The repository root could not be found by walking up from '{AppContext.BaseDirectory}', so the committed "
            + "pose-library skeletons cannot be verified.");
    }

    private static IWebHostEnvironment StubEnvironment(string? webRootPath) => new StubWebHostEnvironment
    {
        WebRootPath = webRootPath!,
        WebRootFileProvider = new NullFileProvider()
    };

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "DreamGenClone.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
