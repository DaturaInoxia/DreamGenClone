using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Proves the probe export produces what the measurement needs, and proves the projected angles are actually
/// different views of the same figure. The second half matters more than the first: an export that wrote five
/// copies of the front view would satisfy every file-format assertion here and make the whole angle feature a
/// lie, so the shoulder span is asserted to collapse toward profile the way a real turn must.
/// </summary>
public sealed class PoseExportProbeTests
{
    [Fact]
    public async Task TheExportWritesKeypointsAndASkeletonForEveryNamedAngle()
    {
        using var fixture = new PoseLibraryTestFixture();
        var target = Path.Combine(Path.GetTempPath(), $"pose-export-{Guid.NewGuid():N}");

        try
        {
            var written = await fixture.Service.ExportProjectedPosesAsync(target);

            var expectedAngles = PoseNamedViews.BodyTargets.Length;
            Assert.Equal(expectedAngles * 2, written.Count);

            foreach (var (label, _) in PoseNamedViews.BodyTargets)
            {
                var slug = PoseNamedViews.Slug(label);

                var json = Path.Combine(target, $"{slug}.json");
                Assert.True(File.Exists(json), $"'{slug}.json' was not written, so the probe has no candidate");

                var person = OpenPosePoseJson.Parse(await File.ReadAllTextAsync(json), slug);
                Assert.Equal(PosePerson.BodyJointCount, person.Body.Count);

                var skeleton = Path.Combine(target, $"{slug}.skeleton.png");
                Assert.True(File.Exists(skeleton), $"'{slug}.skeleton.png' was not written, so nothing can be rendered");

                var bytes = await File.ReadAllBytesAsync(skeleton);
                Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4).ToArray());
            }
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }

    [Fact]
    public async Task TheNamedAnglesAreGenuinelyDifferentViews()
    {
        using var fixture = new PoseLibraryTestFixture();
        var target = Path.Combine(Path.GetTempPath(), $"pose-export-{Guid.NewGuid():N}");

        try
        {
            await fixture.Service.ExportProjectedPosesAsync(target);

            var spans = new Dictionary<string, double>();
            foreach (var (label, _) in PoseNamedViews.BodyTargets)
            {
                var slug = PoseNamedViews.Slug(label);
                var person = OpenPosePoseJson.Parse(
                    await File.ReadAllTextAsync(Path.Combine(target, $"{slug}.json")), slug);
                spans[slug] = ShoulderSpan(person);
            }

            // Turning the figure must foreshorten the shoulder line. If the projection ignored yaw, every angle
            // would report the same span and the tool would still be reporting success.
            var front = spans[PoseNamedViews.Slug("Front")];
            var threeQuarterLeft = spans[PoseNamedViews.Slug("3/4 left")];
            var threeQuarterRight = spans[PoseNamedViews.Slug("3/4 right")];
            var profileLeft = spans[PoseNamedViews.Slug("Profile left")];
            var profileRight = spans[PoseNamedViews.Slug("Profile right")];

            Assert.True(
                front > threeQuarterLeft,
                $"front span {front:0.00} should exceed 3/4 left {threeQuarterLeft:0.00}");
            Assert.True(
                threeQuarterLeft > profileLeft,
                $"3/4 left {threeQuarterLeft:0.00} should exceed profile left {profileLeft:0.00}");
            Assert.True(
                front > threeQuarterRight,
                $"front span {front:0.00} should exceed 3/4 right {threeQuarterRight:0.00}");
            Assert.True(
                threeQuarterRight > profileRight,
                $"3/4 right {threeQuarterRight:0.00} should exceed profile right {profileRight:0.00}");
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }

    /// <summary>
    /// Exports the angles to the git-ignored folder the probe reads, so the measurement has real inputs on disk.
    /// The folder is fixed rather than configurable because it is the path documented in the probe's README; a
    /// path that only the test knew would leave the operator without candidates to measure.
    /// </summary>
    [Fact]
    public async Task TheProbeInputFolderHoldsTheProjectedAngles()
    {
        var folder = Path.Combine(RepositoryRoot(), "artifacts", "tmp", "pose-probes");
        Directory.CreateDirectory(folder);

        using var fixture = new PoseLibraryTestFixture();
        var written = await fixture.Service.ExportProjectedPosesAsync(folder);

        Assert.Equal(PoseNamedViews.BodyTargets.Length * 2, written.Count);
        Assert.All(written, path => Assert.True(File.Exists(path), $"'{path}' was reported written but is missing"));
    }

    private static double ShoulderSpan(PosePerson person)
    {
        var right = person.Body[OpenPosePoseJson.RightShoulderIndex];
        var left = person.Body[OpenPosePoseJson.LeftShoulderIndex];
        return Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "pose-packs"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"The repository root was not found above '{AppContext.BaseDirectory}'.");
    }
}
