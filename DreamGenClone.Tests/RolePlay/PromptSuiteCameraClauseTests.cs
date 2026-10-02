using System.Text.Json;
using System.Text.RegularExpressions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Every prompt in a suite catalog names a camera: a framing and an angle.
///
/// <para>
/// Operator report 2026-10-01: "i feel like the suite prompts are missing the camera angle". They were — 13 of the 45
/// baseline positions named no camera at all, and 27 of 45 Pony variants carried framing and direction but no angle,
/// which is exactly the state Pony's own rule warns about ("without it Pony defaults to overhead/top-down angles").
/// The clause is a REQUIRED component in all three families (SDXL's 17-part anatomy lists Perspective/Viewpoint
/// third-from-last; Pony requires a camera/view tag; the Qwen family contract lists framing) and in this app's own
/// compiler instructions, so a hand-written catalog prompt that omits it is not a stylistic variant — it is a prompt
/// this pipeline would never have produced.
/// </para>
///
/// <para>
/// The check is structural, not eyeballed, and it runs over the REAL catalogs (not fixtures), because the gap
/// appeared in the real ones. EDIT instructions are exempt by construction: they describe a change to an image that
/// already has a camera, so restating framing there would be wrong.
/// </para>
///
/// <para>
/// The vocabulary below is the standard set from
/// <c>.github/instructions/scene-image-prompt-compiler-standards.instructions.md</c>. A new prompt phrased with a
/// camera word that is not listed here should EXTEND this list rather than be excluded from the check.
/// </para>
/// </summary>
public sealed class PromptSuiteCameraClauseTests
{
    /// <summary>What is in the frame: the camera's distance.</summary>
    private static readonly Regex Framing = new(
        @"extreme close-?up|close shot|close-?up|medium close shot|medium close-?up|medium full shot|medium shot|" +
        @"full[- ]body(?: shot)?|full shot|wide shot|cowboy shot|upper body|knees[- ]up|headshot|portrait|" +
        @"macro shot|macro photograph|framed (?:close|tight|tightly|on|at|between|from|across)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Where the camera is: its height, relative to the subject or to the room.</summary>
    private static readonly Regex Angle = new(
        @"eye level|camera angle|low camera|high camera|low angle|high angle|overhead|top[- ]down|bird'?s[- ]eye|" +
        @"from above|from below|from floor|floor level|ground level|bed height|hip height|chest height|" +
        @"shoulder height|waist height|table height|counter height|knee height|worm'?s[- ]eye|dutch angle",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A change to an image that already has a camera, so framing is preserved rather than restated.</summary>
    private static readonly string[] EditVariants = ["qwen-edit-2511", "qwen-image-2.1-edit"];

    [Fact]
    public void EveryCatalogPromptNamesAFramingAndAnAngle()
    {
        var gaps = new List<string>();
        var checkedFields = 0;
        var checkedCatalogs = 0;

        foreach (var catalog in PromptCatalogs())
        {
            checkedCatalogs++;
            var (directory, positions) = catalog;
            foreach (var (id, relativePath) in positions)
            {
                var path = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var position = document.RootElement;

                foreach (var field in new[] { "expected", "neutralScene" })
                {
                    if (!position.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    checkedFields++;
                    Describe(gaps, Path.GetFileName(directory), id, field, value.GetString() ?? string.Empty);
                }

                if (!position.TryGetProperty("variants", out var variants) || variants.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var variant in variants.EnumerateObject())
                {
                    if (EditVariants.Contains(variant.Name, StringComparer.Ordinal)
                        || variant.Value.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    checkedFields++;
                    Describe(gaps, Path.GetFileName(directory), id, $"variants.{variant.Name}", variant.Value.GetString() ?? string.Empty);
                }
            }
        }

        Assert.True(checkedCatalogs > 0, "No prompt catalogs were found under specs/image-generator-tests.");
        Assert.True(checkedFields > 100, $"Only {checkedFields} prompt field(s) were checked, which is too few to be the real catalogs.");
        Assert.True(gaps.Count == 0,
            $"{gaps.Count} prompt field(s) name no camera framing and/or angle:\n  " + string.Join("\n  ", gaps));
    }

    private static void Describe(List<string> gaps, string catalog, string positionId, string field, string text)
    {
        var hasFraming = Framing.IsMatch(text);
        var hasAngle = Angle.IsMatch(text);
        if (hasFraming && hasAngle)
        {
            return;
        }

        var missing = (hasFraming, hasAngle) switch
        {
            (false, false) => "no framing and no angle",
            (true, false) => "no angle",
            _ => "no framing"
        };
        gaps.Add($"{catalog}/{positionId} {field}: {missing}");
    }

    /// <summary>
    /// Every depth-1 catalog folder whose manifest declares positions, with each position's declared path. A manifest
    /// with no positions is a proof record rather than a prompt catalog, and is skipped the same way the importer
    /// skips it. Discovered rather than listed, so a catalog added later is covered without editing this test.
    /// </summary>
    private static IEnumerable<(string Directory, List<(string Id, string Path)> Positions)> PromptCatalogs()
    {
        var root = Path.Combine(RepositoryRoot(), "specs", "image-generator-tests");
        foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(path => path, StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("positions", out var positions)
                || positions.ValueKind != JsonValueKind.Array
                || positions.GetArrayLength() == 0)
            {
                continue;
            }

            var declared = new List<(string, string)>();
            foreach (var entry in positions.EnumerateArray())
            {
                var id = entry.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
                var path = entry.TryGetProperty("path", out var pathValue) ? pathValue.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(path))
                {
                    declared.Add((id!, path!));
                }
            }

            yield return (directory, declared);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root (DreamGenClone.sln) not found above the test output directory.");
    }
}
