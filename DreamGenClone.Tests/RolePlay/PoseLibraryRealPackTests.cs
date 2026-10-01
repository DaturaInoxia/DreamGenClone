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

    [Fact]
    public async Task EveryShippedPoseGetsTheMetadataItsOwnPackDeclares()
    {
        // The real packs, imported into a throwaway database and a throwaway web root: nothing is written into the
        // repository's own pose-packs or wwwroot. This is the test that proves the "all poses" claim — if a category
        // in any pack lacks a declaration, its poses come back as "not declared" and this fails with their names.
        using var fixture = PoseLibraryTestFixture.ForExistingPacksRoot(PacksRoot());

        var imported = await fixture.Importer.ImportAsync();
        Assert.Empty(imported.Skipped);
        Assert.Equal(0, imported.MetadataFilled);

        var presets = await fixture.Repository.ListAsync();
        Assert.True(presets.Count >= 570, $"the packs should hold the recorded 579 poses but yielded {presets.Count}");

        var undeclared = presets
            .Where(preset => preset.ContentRating == PoseContentRating.Unrated
                || preset.Stance == PoseStance.Unknown
                || preset.MetadataPrompt.Length == 0)
            .Select(preset => $"{preset.LibraryId}/{preset.Category}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(undeclared);

        // What the keypoint measurement DISAGREES with is deliberately NOT asserted here: the disagreements are pinned
        // by count in PoseMetadataAuditTests, which also writes the report naming each pose and quoting the numbers
        // behind it. Two tests asserting the same set is how they quietly drift apart.
    }

    [Fact]
    public async Task TheDeclarationsThatOnlyThePackKnowsReachTheStoredPoses()
    {
        // Three things the keypoints cannot state, each checked on the data that ships: the camera of a from-above
        // pack, the facing of the one face-down pack, and the rating that decides the prompt's subject.
        using var fixture = PoseLibraryTestFixture.ForExistingPacksRoot(PacksRoot());
        await fixture.Importer.ImportAsync();

        var presets = await fixture.Repository.ListAsync();

        var onStomach = presets.Where(preset => preset.LibraryId == "openpose-from-above-stomach").ToArray();
        Assert.NotEmpty(onStomach);
        Assert.All(onStomach, preset =>
        {
            Assert.Equal(PoseCameraAngle.FromAbove, preset.CameraAngle);
            Assert.Equal(PoseFacingDirection.Back, preset.Direction);
            Assert.Contains("lying face down", preset.MetadataPrompt, StringComparison.Ordinal);
            Assert.Contains("viewed from above", preset.MetadataPrompt, StringComparison.Ordinal);
        });

        var nsfw = presets.Where(preset => preset.LibraryId == PoseLibraryIds.BundledPackFolder).ToArray();
        Assert.NotEmpty(nsfw);
        Assert.All(nsfw, preset =>
        {
            Assert.Equal(PoseContentRating.Nsfw, preset.ContentRating);
            Assert.Contains("a naked woman", preset.MetadataPrompt, StringComparison.Ordinal);
        });

        var sfw = presets.Where(preset => preset.LibraryId != PoseLibraryIds.BundledPackFolder).ToArray();
        Assert.NotEmpty(sfw);
        Assert.All(sfw, preset =>
        {
            Assert.Equal(PoseContentRating.Sfw, preset.ContentRating);
            Assert.DoesNotContain("naked", preset.MetadataPrompt, StringComparison.Ordinal);
        });
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
