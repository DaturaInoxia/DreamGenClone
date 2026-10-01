using System.Text;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The metadata audit over the packs that ship, run as a test because the classification it audits lives in C# and a
/// second implementation of it in a script would be a second answer to the same question.
///
/// It writes a full report to <c>artifacts/tmp/pose-metadata-audit/report.md</c> and then asserts the two things the
/// report is for:
///
///   * COMPLETENESS — every pose in every pack has a rating, a stance and a prompt. A gap here is a defect: the pose
///     would render with no wording, which is what this whole feature exists to stop.
///   * THE FLAGGED SET, PINNED — a pose whose keypoints disagree with its pack's declaration is listed by name with
///     the measured numbers behind it. The list is ALLOWED to be non-empty (a real pack contains poses that lean, and
///     a lean is not a lie) but it is pinned, so a new disagreement fails here instead of accumulating quietly.
/// </summary>
public sealed class PoseMetadataAuditTests
{
    [Fact]
    public async Task TheShippedPacksReportCompletenessAndTheReviewedDisagreements()
    {
        var packsRoot = PacksRoot();
        using var fixture = PoseLibraryTestFixture.ForExistingPacksRoot(packsRoot);

        var imported = await fixture.Importer.ImportAsync();
        Assert.Empty(imported.Skipped);

        var presets = await fixture.Repository.ListAsync();
        Assert.True(presets.Count >= 570, $"the packs should hold the recorded 579 poses but yielded {presets.Count}");

        var report = new StringBuilder();
        report.AppendLine("# Pose metadata audit");
        report.AppendLine();
        report.AppendLine($"Generated {DateTime.UtcNow:u} from `pose-packs/` — {presets.Count} poses.");
        report.AppendLine();

        var incomplete = new List<string>();
        var flagged = new List<string>();
        var promptSamples = new List<string>();

        var byPack = presets
            .GroupBy(preset => preset.LibraryId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var pack in byPack)
        {
            var poses = pack.OrderBy(preset => preset.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            report.AppendLine($"## {pack.Key} — {poses.Length} poses");
            report.AppendLine();
            report.AppendLine("| category | rating | stance | direction | camera | flagged |");
            report.AppendLine("|---|---|---|---|---|---|");

            foreach (var group in poses.GroupBy(preset => preset.Category, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var distinct = group
                    .Select(preset => $"{(preset.ContentRating == PoseContentRating.Unrated ? "UNRATED" : PoseMetadataLabels.Rating(preset.ContentRating))} / "
                        + $"{PoseMetadataLabels.Stance(preset.Stance)} / "
                        + $"{PoseMetadataLabels.Direction(preset.Direction)} / "
                        + $"{preset.CameraAngle}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var flags = group.Count(preset => preset.MetadataNeedsReview);
                report.AppendLine(
                    $"| {group.Key} | {string.Join(" ; ", distinct)} | {group.Count()} poses | "
                    + $"{group.Count(preset => preset.MetadataPrompt.Length == 0)} empty prompts | "
                    + $"{flags} |");
            }

            report.AppendLine();

            foreach (var preset in poses)
            {
                if (preset.ContentRating == PoseContentRating.Unrated
                    || preset.Stance == PoseStance.Unknown
                    || preset.MetadataPrompt.Length == 0)
                {
                    incomplete.Add(
                        $"{preset.LibraryId}/{preset.Category}/{preset.Name}: rating {preset.ContentRating}, "
                        + $"stance {preset.Stance}, prompt {(preset.MetadataPrompt.Length == 0 ? "empty" : "present")}");
                }

                if (preset.MetadataNeedsReview)
                {
                    flagged.Add($"{preset.LibraryId}/{preset.Category}/{preset.Name}: {preset.MetadataReviewNote}");
                }

                if (promptSamples.Count < 400)
                {
                    promptSamples.Add($"{preset.LibraryId}/{preset.Category}/{preset.Name}\t{preset.MetadataPrompt}");
                }
            }
        }

        report.AppendLine("## Disagreements (measurement vs declaration)");
        report.AppendLine();
        report.AppendLine(flagged.Count == 0 ? "None." : string.Join(Environment.NewLine, flagged.Select(line => $"- {line}")));
        report.AppendLine();

        report.AppendLine("## Prompts");
        report.AppendLine();
        report.AppendLine("```");
        report.AppendLine(string.Join(Environment.NewLine, promptSamples));
        report.AppendLine("```");

        var reportPath = Path.Combine(RepositoryRoot(packsRoot), "artifacts", "tmp", "pose-metadata-audit", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, report.ToString());

        // Every pose is usable: it has a subject to render, a stance to read and a prompt to send.
        Assert.Empty(incomplete);

        // The disagreements are PINNED, per category, to the set that was read and accepted on 2026-09-30. They are
        // pinned with their counts rather than merely listed, so a pose that starts disagreeing in a category that
        // already disagrees still fails here instead of hiding inside a known-bad category name.
        var actual = presets
            .Where(preset => preset.MetadataNeedsReview)
            .GroupBy(preset => $"{preset.LibraryId}/{preset.Category}", StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key} ({group.Count()})")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var expected = ReviewedDisagreements
            .Select(entry => $"{entry.Library}/{entry.Category} ({entry.Count})")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// The disagreements the shipped packs really contain, counted per category and read on 2026-09-30 (the poses are
    /// named, with their measured numbers, in the report this test writes).
    ///
    /// Both kinds are honest facts about the pack rather than defects in it: a pose whose shoulders are ordered like a
    /// back view contradicts its declared facing, and a pose whose torso measures far off vertical is a body that
    /// LEANS, which the prompt's stance word does not convey. Neither one changes what the app stores — the pack's
    /// declaration still wins — so this list exists to be looked at, and to fail loudly when it grows.
    /// </summary>
    private static readonly (string Library, string Category, int Count)[] ReviewedDisagreements =
    [
        ("openpose-nsfw", "all_fours", 1),
        ("openpose-nsfw", "kneeling", 5),
        ("openpose-nsfw", "sitting", 8),
        ("openpose-nsfw", "squatting", 2),
        ("openpose-nsfw", "standing", 28),
        ("openposes-collection", "sitting", 2),
        ("openposes-collection", "standing", 2)
    ];

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
            $"The pose-packs folder was not found above '{AppContext.BaseDirectory}'.");
    }

    private static string RepositoryRoot(string packsRoot) =>
        Directory.GetParent(packsRoot)?.FullName
        ?? throw new InvalidOperationException($"'{packsRoot}' has no parent directory.");
}
