using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// TEMPORARY measurement probe — delete once the numbers are recorded.
///
/// The operator reports that loading a library pose and pressing turn-left ONCE replaces the skeleton with a
/// different figure rather than turning it 5°. Two causes fit that report, and they have different fixes:
///
/// (a) PRESENTATION — the first press swaps the library's own skeleton PNG for the rig's projection, which is a
///     different DRAWING (the pack's annotator output carries hands and a face; the rig has neither). The pose
///     could be identical and the picture would still look like a different skeleton.
/// (b) GEOMETRY — the fit matches the rig to ONE 2D view, which does not determine the 3D pose. Many rig poses
///     project to nearly the same picture from one viewpoint, so the fitted rig can be a different 3D figure that
///     only coincided at the view it was fitted to. Turning it then walks away from the source pose, which is a
///     "different skeleton" no matter how it is drawn.
///
/// (b) is measured against a control: the same 5° step applied to a pose the rig itself produced, which is what a
/// 5° turn is SUPPOSED to cost. If a fitted pose costs several times the control, the turn is not a turn.
/// </summary>
public sealed class PoseTurnProbeTests
{
    [Fact]
    public void MeasureOneStepOnAFittedPose()
    {
        var settings = Settings();
        var mannequin = PoseMannequin.Standing();
        var reports = new List<string>();

        // Control: the magnitude a 5° turn HAS, on ground truth the rig produced itself.
        foreach (var yaw in new[] { 0.0, 30.0, 60.0, 90.0 })
        {
            var before = PoseProjection.Project(
                mannequin, mannequin.StandingStance(), new PoseView(YawDegrees: yaw), settings);
            var after = PoseProjection.Project(
                mannequin, mannequin.StandingStance(), new PoseView(YawDegrees: yaw + 5), settings);

            reports.Add(
                $"CONTROL rig-native {yaw,5:0}° yaw : one step moves {MeanStepPercentOfHeight(before, after),6:0.00}% of height");
        }

        reports.Add(string.Empty);
        var target = ProbeFolder();

        foreach (var (label, relative) in Poses())
        {
            var observed = OpenPosePoseJson.Parse(File.ReadAllText(PackPath(relative)), relative);
            var fit = PoseRigFit.Fit(observed, settings);

            var turnedView = PoseProjection.Step(fit.View, PoseRotationAxis.Yaw, 1, settings);
            var turned = PoseProjection.Project(mannequin, fit.Rotations, turnedView, settings);

            // THE NUMBER THAT MATTERS: what the first press costs, in the same units as the control.
            var firstPress = MeanStepPercentOfHeight(fit.Reprojection, turned);

            reports.Add(
                $"{label,-12} residual {fit.MeanErrorPercentOfHeight,5:0.00}%  "
                + $"fitted yaw {fit.View.YawDegrees,7:0.0}°  ambiguous {fit.OrientationAmbiguous,-5}  "
                + $"FIRST PRESS {firstPress,6:0.00}% of height");

            // The pictures, so the presentation half can be judged by eye rather than argued about.
            File.WriteAllBytes(Path.Combine(target, $"{label}-1-source.png"), PoseSkeletonRenderer.RenderPng(observed));
            File.WriteAllBytes(Path.Combine(target, $"{label}-2-fitted.png"), PoseSkeletonRenderer.RenderPng(fit.Reprojection));
            File.WriteAllBytes(Path.Combine(target, $"{label}-3-turned5.png"), PoseSkeletonRenderer.RenderPng(turned));
        }

        File.WriteAllLines(Path.Combine(target, "turn-probe.txt"), reports);
        Assert.True(reports.Count > 0);
    }

    private static (string Label, string Relative)[] Poses() =>
    [
        ("standing028", "NSFW_standing/512768/NSFW_standing028.json"),
        ("squatting029", "NSFW_Squatting/512512/NSFW_Squatting029.json"),
        ("kneeling017", "NSFW_Kneeling/512768/NSFW_Kneeling017.json")
    ];

    /// <summary>
    /// Mean joint movement between two projections of the SAME figure at the SAME camera, as a percentage of the
    /// figure's height. Both sides are in one canvas and one scale, so the pixels are directly comparable.
    /// </summary>
    private static double MeanStepPercentOfHeight(PosePerson before, PosePerson after)
    {
        var count = Math.Min(before.Body.Count, after.Body.Count);
        if (count == 0) return 0;

        var height = Height(before);
        var total = 0.0;

        for (var index = 0; index < count; index++)
        {
            total += Math.Sqrt(
                Math.Pow(after.Body[index].X - before.Body[index].X, 2)
                + Math.Pow(after.Body[index].Y - before.Body[index].Y, 2));
        }

        return total / count / height * 100.0;
    }

    private static double Height(PosePerson person)
    {
        var ys = person.Body.Select(point => point.Y).ToArray();
        var extent = ys.Max() - ys.Min();

        return extent > 0 ? extent : 1.0;
    }

    private static string ProbeFolder()
    {
        var folder = Path.Combine(RepositoryRoot(), "artifacts", "tmp", "pose-probes", "turn");
        Directory.CreateDirectory(folder);

        return folder;
    }

    private static string PackPath(string relativePath) => Path.Combine(
        RepositoryRoot(), "pose-packs", PoseLibraryIds.BundledPackFolder,
        relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static PoseStudioOptions Settings() => new()
    {
        FocalLengthPx = 1600,
        CameraDistance = 4.5,
        Canvas = 1024,
        RotationStepDegrees = 5
    };

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "pose-packs"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"The repository root was not found above '{AppContext.BaseDirectory}'.");
    }
}
