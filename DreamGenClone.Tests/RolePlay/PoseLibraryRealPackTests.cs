using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Proves the C# pose reader and renderer against the REAL git-tracked pack, not a fixture. The fixture tests
/// prove the logic; these prove the logic survives the data that actually ships, including the three stance
/// paths the importer hardcodes as known-good — if one of those paths ever moves, this test says so instead of
/// the library silently losing its only verified poses.
/// </summary>
public sealed class PoseLibraryRealPackTests
{
    [Fact]
    public void EveryPackFolderDeclaresItselfWithAManifest()
    {
        var packsRoot = PacksRoot();
        var packFolders = Directory.GetDirectories(packsRoot);

        Assert.NotEmpty(packFolders);
        foreach (var folder in packFolders)
        {
            var manifest = Path.Combine(folder, "pack.json");
            Assert.True(File.Exists(manifest),
                $"pack '{Path.GetFileName(folder)}' has no pack.json, so the importer would refuse it by name");
        }
    }

    [Fact]
    public void TheBundledPackIsImportedAsItsOwnLibrary()
    {
        // The bundled pack is a folder under the packs root, which is what makes it a library rather than a
        // special case in code.
        var expected = Path.Combine(PacksRoot(), PoseLibraryIds.BundledPackFolder);

        Assert.True(Directory.Exists(expected), $"the bundled pack folder '{expected}' is missing");
        Assert.True(File.Exists(Path.Combine(expected, "pack.json")),
            "the bundled pack needs its manifest so its library has a name and a provenance");
    }

    [Fact]
    public void EveryPackFileParsesAsOnePersonWithAFullBody()
    {
        var packRoot = Path.Combine(PacksRoot(), PoseLibraryIds.BundledPackFolder);
        var files = Directory.GetFiles(packRoot, "*.json", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetFileName(file), "pack.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(files.Length >= 400, $"the pack should hold the recorded ~472 poses but holds {files.Length}");

        var failures = new List<string>();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(packRoot, file).Replace('\\', '/');
            try
            {
                var person = OpenPosePoseJson.Parse(File.ReadAllText(file), relative);
                Assert.Equal(PosePerson.BodyJointCount, person.Body.Count);
            }
            catch (Exception ex)
            {
                failures.Add($"{relative}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(BodyReferenceStance.Standing)]
    [InlineData(BodyReferenceStance.Squatting)]
    [InlineData(BodyReferenceStance.Kneeling)]
    public void TheVerifiedStanceSkeletons_ExistParseAndRender(BodyReferenceStance stance)
    {
        var packRoot = Path.Combine(PacksRoot(), PoseLibraryIds.BundledPackFolder);
        var skeleton = BodyStanceSkeletons.Require(stance);
        var path = Path.Combine(packRoot, skeleton.VerifiedOn.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(path), $"the verified source pose '{skeleton.VerifiedOn}' is missing from the pack");

        var person = OpenPosePoseJson.Parse(File.ReadAllText(path), skeleton.VerifiedOn);
        OpenPosePoseJson.RequireHeadKeypoints(person, skeleton.VerifiedOn);

        var bytes = PoseSkeletonRenderer.RenderPng(person);

        Assert.True(bytes.Length > 0);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], bytes.Take(4).ToArray());
    }

    /// <summary>
    /// Finds the packs root by walking up from the test binaries to the repository root, so the test does not
    /// depend on the working directory a runner happens to use.
    /// </summary>
    private static string PacksRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "pose-packs");
            if (Directory.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"The pose-packs folder was not found above '{AppContext.BaseDirectory}'. It is required for this "
            + "test and for the library import; check out 'pose-packs'.");
    }
}
