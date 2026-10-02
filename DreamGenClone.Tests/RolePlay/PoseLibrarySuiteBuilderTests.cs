using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The POSE LIBRARY as a suite: one cell per pose, derived rather than authored (B-135).
///
/// <para>
/// Three things are pinned here because each has a way of failing quietly. A cell must record the POSE it stands for
/// (by id) rather than a copy of the angles, so a corrected direction takes effect on the next run. Rebuilding must be
/// idempotent — the same suite, the same cells — because a suite that grew by one copy per rebuild would render the same
/// pose several times and look like a bigger library. And a pose whose metadata cannot justify a reference must be
/// VISIBLE with its reason, not dropped: a pose missing from the suite and a pose the library never had look identical.
/// </para>
/// </summary>
public sealed class PoseLibrarySuiteBuilderTests
{
    private const string LibraryId = "library-nsfw";

    [Fact]
    public async Task BuildAsync_DerivesOneCellPerPoseWithItsPoseBindingAndPrompt()
    {
        var (builder, repository, library) = Build(
            Pose("all_fours 001", PoseStance.AllFours, PoseFacingDirection.Back, PoseContentRating.Nsfw),
            Pose("standing 01", PoseStance.Standing, PoseFacingDirection.Front, PoseContentRating.Sfw));

        var reports = await builder.BuildAsync(new PoseLibrarySuiteRequest(LibraryId));

        var report = Assert.Single(reports);
        Assert.Equal("Pose Library · OpenPose NSFW pack", report.SuiteName);
        Assert.Equal(2, report.CellCount);
        Assert.Empty(report.Problems);

        var cells = await repository.ListCellsAsync(report.SuiteId);
        Assert.Equal(2, cells.Count);
        Assert.All(cells, cell => Assert.Equal(report.SuiteId, cell.SuiteId));
        Assert.Equal([0, 1], cells.Select(cell => cell.Ordinal));

        // The cell names the POSE, not the angles: the render resolves the face and body angle from the preset's current
        // metadata, so a direction corrected after this build is honoured by the next run.
        var first = cells.Single(cell => cell.Name == "all_fours 001");
        var binding = JsonSerializer.Deserialize<List<ReferenceApplicationSelection>>(first.BindingsJson)!.Single();
        Assert.Equal("Pose", binding.Kind);
        Assert.Equal("PoseLibrarySkeleton", binding.Source);
        Assert.Equal(library.Presets[0].Id, binding.PosePresetId);
        Assert.Equal("poses/all_fours_001.png", binding.SkeletonRelativePath);

        // The pose's OWN prompt, and a declared seed, so a reproducible run has something to reproduce.
        Assert.Contains("all_fours 001", first.ExpectedPrompt, StringComparison.Ordinal);
        Assert.Equal(PoseLibrarySuiteBuilder.SeedBase, JsonDocument.Parse(first.SettingsJson).RootElement.GetProperty("seed").GetInt64());

        // No per-model variants: the wording is the pose's own and every model renders it as written.
        Assert.Equal("{}", first.VariantsJson);
    }

    [Fact]
    public async Task BuildAsync_IsIdempotent_AndRemovesTheCellsOfPosesThatAreGone()
    {
        var (builder, repository, library) = Build(
            Pose("standing 01", PoseStance.Standing, PoseFacingDirection.Front, PoseContentRating.Sfw),
            Pose("standing 02", PoseStance.Standing, PoseFacingDirection.Front, PoseContentRating.Sfw));

        var first = Assert.Single(await builder.BuildAsync(new PoseLibrarySuiteRequest(LibraryId)));
        var firstCells = await repository.ListCellsAsync(first.SuiteId);
        var survivorCellId = firstCells.Single(cell => cell.Name == "standing 01").Id;
        var doomedCellId = firstCells.Single(cell => cell.Name == "standing 02").Id;

        // A pose is deleted from the library, and another is renamed.
        library.Presets.RemoveAt(1);
        library.Presets[0].Name = "standing 01 (renamed)";

        var second = Assert.Single(await builder.BuildAsync(new PoseLibrarySuiteRequest(LibraryId)));

        Assert.Equal(first.SuiteId, second.SuiteId);
        Assert.Equal(1, second.CellCount);
        Assert.Equal(1, second.RemovedCells);
        Assert.Equal(doomedCellId, Assert.Single(repository.DeletedCellIds));

        // The surviving cell kept its id (and took the new name), so a stored reference to it still means the same pose.
        var surviving = Assert.Single(await repository.ListCellsAsync(second.SuiteId));
        Assert.Equal(survivorCellId, surviving.Id);
        Assert.Equal("standing 01 (renamed)", surviving.Name);
    }

    [Fact]
    public async Task BuildAsync_KeepsAPoseItCannotPlan_AndSaysWhatIsMissing()
    {
        var (builder, repository, library) = Build(
            Pose("undeclared 01", PoseStance.Standing, PoseFacingDirection.Unknown, PoseContentRating.Sfw),
            Pose("unrated 01", PoseStance.Standing, PoseFacingDirection.Front, PoseContentRating.Unrated));

        var report = Assert.Single(await builder.BuildAsync(new PoseLibrarySuiteRequest(LibraryId)));

        Assert.Equal(2, report.CellCount);
        Assert.Equal(2, report.Problems.Count);
        Assert.Contains(report.Problems, problem => problem.Contains("undeclared 01") && problem.Contains("direction"));
        Assert.Contains(report.Problems, problem => problem.Contains("unrated 01") && problem.Contains("rating"));

        // The reason rides on the CELL too, so a run shows it against the pose rather than only in the build report.
        var cells = await repository.ListCellsAsync(report.SuiteId);
        var undeclared = cells.Single(cell => cell.Name == "undeclared 01");
        Assert.Contains("direction", undeclared.ProblemsJson, StringComparison.OrdinalIgnoreCase);

        // An unrated pose has no stored prompt at all (a prompt cannot be written without knowing whether the subject is
        // clothed), and the cell says that instead of carrying an empty prompt that would render as nothing.
        Assert.Empty(cells.Single(cell => cell.Name == "unrated 01").ExpectedPrompt);

        // ... and the caller can ask for them to be left out entirely, which is a stated choice rather than a default.
        var (strictBuilder, _, _) = Build(
            Pose("undeclared 01", PoseStance.Standing, PoseFacingDirection.Unknown, PoseContentRating.Sfw));
        var strict = Assert.Single(await strictBuilder.BuildAsync(
            new PoseLibrarySuiteRequest(LibraryId, IncludeUnplannable: false)));
        Assert.Equal(0, strict.CellCount);
    }

    [Fact]
    public async Task BuildAsync_BuildsOneSuitePerLibrary()
    {
        var (builder, repository, library) = Build(
            Pose("standing 01", PoseStance.Standing, PoseFacingDirection.Front, PoseContentRating.Sfw));
        library.Libraries.Add(new PoseLibrary { Id = "library-collection", Name = "OpenPoses Collection" });
        var collectionPose = Pose("dance 01", PoseStance.Dancing, PoseFacingDirection.Front, PoseContentRating.Sfw);
        collectionPose.LibraryId = "library-collection";
        library.Presets.Add(collectionPose);

        var reports = await builder.BuildAsync(new PoseLibrarySuiteRequest());

        Assert.Equal(2, reports.Count);
        Assert.Equal(
            ["Pose Library · OpenPose NSFW pack", "Pose Library · OpenPoses Collection"],
            reports.Select(report => report.SuiteName).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(reports, report => Assert.Equal(1, report.CellCount));
        Assert.Equal(2, (await repository.ListSuitesAsync()).Count);
    }

    private static (PoseLibrarySuiteBuilder Builder, RecordingSuiteRepository Repository, StubPoseLibraryService Library)
        Build(params PosePreset[] presets)
    {
        var library = new StubPoseLibraryService();
        library.Libraries.Add(new PoseLibrary { Id = LibraryId, Name = "OpenPose NSFW pack" });
        foreach (var preset in presets)
        {
            preset.LibraryId = LibraryId;
            library.Presets.Add(preset);
        }

        var repository = new RecordingSuiteRepository();
        var builder = new PoseLibrarySuiteBuilder(library, repository, NullLogger<PoseLibrarySuiteBuilder>.Instance);
        return (builder, repository, library);
    }

    private static PosePreset Pose(
        string name,
        PoseStance stance,
        PoseFacingDirection direction,
        PoseContentRating rating) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Category = name.Split(' ')[0],
        SkeletonPngPath = $"poses/{name.Replace(' ', '_').Replace("(", string.Empty).Replace(")", string.Empty)}.png",
        KnownGood = true,
        Stance = stance,
        Direction = direction,
        CameraAngle = PoseCameraAngle.EyeLevel,
        ContentRating = rating,
        // The stored prompt the render sends: composed once at declaration time, exactly as the pose card shows it.
        MetadataPrompt = rating == PoseContentRating.Unrated
            ? string.Empty
            : $"A full-body photograph of a {(rating == PoseContentRating.Nsfw ? "naked" : "clothed")} woman, "
                + $"{name}, natural skin texture, photorealistic, plain studio background, 85mm."
    };
}
