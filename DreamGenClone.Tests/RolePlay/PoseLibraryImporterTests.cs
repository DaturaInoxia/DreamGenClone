using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The importer is what makes the library exist, so it is pinned on the two properties the plan calls out:
/// re-running it changes nothing, and it never overwrites metadata the operator has edited.
/// </summary>
public sealed class PoseLibraryImporterTests
{
    /// <summary>The one pack path measured to hold under the control model, so the one that may be known-good.</summary>
    private const string VerifiedPath = "NSFW_standing/512768/NSFW_standing028.json";

    /// <summary>
    /// A BUNDLED pack must be present without being asked for. Before this existed, a pack that ships with the app sat
    /// on disk while the table was empty, so a correct empty-result search read as a broken one — reported as "Search
    /// does nothing" (2026-09-25), with the pack on disk and zero rows in the store.
    /// </summary>
    [Fact]
    public async Task EnsureBundledPack_SeedsAnEmptyLibrary_AndThenDoesNothing()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WritePose("NSFW_standing/512768/NSFW_standing028.json");

        var seeded = await fixture.Importer.EnsureBundledPackAsync();

        Assert.NotNull(seeded);
        Assert.Equal(1, seeded!.PresetsImported);

        // Idempotent, which is what makes it safe to call before every search: the second visit must do nothing.
        Assert.Null(await fixture.Importer.EnsureBundledPackAsync());

        var presets = await fixture.Repository.ListAsync();
        Assert.Single(presets);
        Assert.Equal(PoseLibraryIds.BundledPackFolder, presets[0].LibraryId);
    }

    [Fact]
    public async Task Import_IsIdempotent_AndRendersSkeletonsOnce()
    {
        using var fixture = new Fixture();
        fixture.WritePose("NSFW_standing/512768/NSFW_standing028.json");
        fixture.WritePose("NSFW_Kneeling/512768/NSFW_kneeling099.json");

        var first = await fixture.Importer.ImportAsync();
        var second = await fixture.Importer.ImportAsync();

        Assert.Equal(2, first.PresetsImported);
        Assert.Equal(2, first.SkeletonsRendered);
        Assert.Empty(first.Skipped);

        Assert.Equal(0, second.PresetsImported);
        Assert.Equal(2, second.PresetsAlreadyPresent);
        Assert.Equal(0, second.SkeletonsRendered);

        var presets = await fixture.Repository.ListAsync();
        Assert.Equal(2, presets.Count);
        Assert.All(presets, preset => Assert.Equal(PoseLibraryIds.BundledPackFolder, preset.LibraryId));

        var library = await fixture.Repository.GetLibraryAsync(PoseLibraryIds.BundledPackFolder);
        Assert.NotNull(library);
        Assert.True(library!.IsSystem);
        Assert.Equal("Test pack", library.Name);
    }

    [Fact]
    public async Task Import_TagsKnownGoodOnlyFromRecordedEvidence()
    {
        using var fixture = new Fixture();
        fixture.WritePose(VerifiedPath);
        fixture.WritePose("NSFW_Kneeling/512768/NSFW_kneeling099.json");

        await fixture.Importer.ImportAsync();
        var presets = await fixture.Repository.ListAsync();

        var verified = presets.Single(preset => preset.Name == "standing 028");
        var unverified = presets.Single(preset => preset.Name == "kneeling 099");

        Assert.True(verified.KnownGood);
        Assert.False(unverified.KnownGood);
    }

    [Fact]
    public async Task Import_DoesNotOverwriteAnEditedPreset()
    {
        using var fixture = new Fixture();
        fixture.WritePose(VerifiedPath);
        await fixture.Importer.ImportAsync();

        var preset = (await fixture.Repository.ListAsync()).Single();
        preset.Name = "Renamed by the operator";
        preset.Keywords = "renamed";
        await fixture.Repository.UpsertAsync(preset);

        await fixture.Importer.ImportAsync();

        var reloaded = (await fixture.Repository.ListAsync()).Single();
        Assert.Equal("Renamed by the operator", reloaded.Name);
        Assert.Equal("renamed", reloaded.Keywords);
    }

    [Fact]
    public async Task Import_ReportsUnusableFilesWithTheirReason()
    {
        using var fixture = new Fixture();
        fixture.WritePose(VerifiedPath);
        fixture.WriteRaw("NSFW_broken/bad.json", "{ not json");
        fixture.WriteRaw("NSFW_pair/pair.json", $$"""
            { "people": [ {{Person()}}, {{Person()}} ] }
            """);

        var result = await fixture.Importer.ImportAsync();

        Assert.Equal(1, result.PresetsImported);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Contains(result.Skipped, entry => entry.Contains("NSFW_broken/bad.json", StringComparison.Ordinal));
        Assert.Contains(result.Skipped, entry => entry.Contains("exactly one person", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_RecordsProvenanceWithTheSourceHash()
    {
        using var fixture = new Fixture();
        fixture.WritePose(VerifiedPath);

        await fixture.Importer.ImportAsync();

        var preset = (await fixture.Repository.ListAsync()).Single();
        Assert.NotNull(preset.ProvenanceJson);
        Assert.Contains(VerifiedPath, preset.ProvenanceJson!, StringComparison.Ordinal);
        Assert.Contains("sha256", preset.ProvenanceJson!, StringComparison.Ordinal);
        Assert.NotNull(preset.SkeletonPngPath);
        Assert.True(File.Exists(Path.Combine(fixture.WebRoot, "pose-library", preset.SkeletonPngPath!)),
            "the rendered skeleton should exist under the configured folder");
    }

    [Fact]
    public async Task Import_WithoutConfiguredPacksRoot_FailsFast()
    {
        using var fixture = new PoseLibraryTestFixture(configurePackRoot: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Importer.ImportAsync());

        Assert.Contains("PacksRoot", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_WhenTheConfiguredPacksRootIsMissing_FailsFast()
    {
        using var fixture = new PoseLibraryTestFixture(configurePackRoot: false);
        fixture.OptionsValue.PacksRoot = "does/not/exist";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Importer.ImportAsync());

        Assert.Contains("does not exist", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_APackWithoutAManifest_IsRefusedByName()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WritePose(VerifiedPath);
        File.Delete(Path.Combine(fixture.PackRoot, "pack.json"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Importer.ImportAsync());

        Assert.Contains("pack.json", error.Message, StringComparison.Ordinal);
        Assert.Contains(PoseLibraryIds.BundledPackFolder, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportPack_ForOneFolder_LeavesTheOtherPacksAlone()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WritePose(VerifiedPath);
        fixture.AddPack("second-pack", "Second pack", "NSFW_sitting/512512/NSFW_sitting003.json");

        var result = await fixture.Importer.ImportPackAsync("second-pack");

        Assert.Equal(1, result.Packs);
        Assert.Equal(1, result.PresetsImported);

        var presets = await fixture.Repository.ListAsync();
        Assert.Single(presets);
        Assert.Equal("second-pack", presets[0].LibraryId);
    }

    private static string Person() =>
        $$"""{ "pose_keypoints_2d": [{{string.Join(", ", Enumerable.Range(0, 18).SelectMany(i => new[] { i * 10, 100 + i, 1.0 }))}}] }""";

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        private readonly string _dbPath;

        public Fixture(bool configurePackRoot = true)
        {
            _root = Path.Combine(Path.GetTempPath(), $"pose-import-{Guid.NewGuid():N}");
            PacksRoot = Path.Combine(_root, "packs");
            PackRoot = Path.Combine(PacksRoot, PoseLibraryIds.BundledPackFolder);
            WebRoot = Path.Combine(_root, "web");
            Directory.CreateDirectory(PackRoot);
            Directory.CreateDirectory(WebRoot);

            File.WriteAllText(
                Path.Combine(PackRoot, "pack.json"),
                """{ "name": "Test pack", "description": "Fixture pack", "source": "test" }""");

            _dbPath = Path.Combine(_root, "poses.db");
            Repository = new PosePresetRepository(
                Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={_dbPath};Pooling=False" }));

            OptionsValue = new PoseLibraryOptions
            {
                PacksRoot = configurePackRoot ? PacksRoot : null,
                SkeletonFolder = "library",
                DownloadMaxMegabytes = 64
            };

            Importer = new PoseLibraryImporter(
                Repository,
                new StubHostEnvironment(WebRoot, _root),
                Options.Create(OptionsValue),
                NullLogger<PoseLibraryImporter>.Instance);

            Service = new PoseLibraryService(
                Repository,
                new StubHostEnvironment(WebRoot, _root),
                Options.Create(OptionsValue),
                Options.Create(new PoseStudioOptions
                {
                    FocalLengthPx = 1600,
                    CameraDistance = 4.5,
                    Canvas = 1024,
                    RotationStepDegrees = 5
                }));
        }

        public string PacksRoot { get; }

        public string PackRoot { get; }

        public string WebRoot { get; }

        public PoseLibraryOptions OptionsValue { get; }

        public PosePresetRepository Repository { get; }

        public PoseLibraryImporter Importer { get; }

        public PoseLibraryService Service { get; }

        public void WritePose(string relativePath) => WriteRaw(relativePath, PackDocument());

        /// <summary>
        /// Adds another pack under the same packs root, so "a second pack" is a real directory rather than a
        /// second library row that could never occur in practice.
        /// </summary>
        public string AddPack(string packFolder, string name, params string[] relativePaths)
        {
            var directory = Path.Combine(PacksRoot, packFolder);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "pack.json"),
                $$"""{ "name": "{{name}}", "source": "test" }""");

            foreach (var relative in relativePaths)
            {
                var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, PackDocument());
            }

            return directory;
        }

        public void WriteRaw(string relativePath, string content)
        {
            var path = Path.Combine(PackRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        private static string PackDocument()
        {
            var body = Enumerable.Range(0, 18)
                .SelectMany(i => new[] { (double)(i * 10), (double)(100 + i), 1.0 })
                .ToArray();

            return $$"""
                { "people": [ { "pose_keypoints_2d": [{{string.Join(", ", body)}}] } ] }
                """;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp folder is not a test failure.
            }
        }
    }

    private sealed class StubHostEnvironment : IWebHostEnvironment
    {
        public StubHostEnvironment(string webRoot, string contentRoot)
        {
            WebRootPath = webRoot;
            ContentRootPath = contentRoot;
        }

        public string WebRootPath { get; set; }

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ApplicationName { get; set; } = "DreamGenClone.Tests";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string EnvironmentName { get; set; } = Environments.Development;
    }
}
