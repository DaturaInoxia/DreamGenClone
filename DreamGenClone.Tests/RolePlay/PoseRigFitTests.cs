using System.Numerics;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The rig fit is what lets a library pose be opened in the editor, so these pin the two things that decide
/// whether it is trustworthy: it converges when a solution exists, and it reports an honest residual on real
/// library poses — whose proportions differ from the rig's, so a perfect fit is not expected and not claimed.
/// </summary>
public sealed class PoseRigFitTests
{
    /// <summary>
    /// Ground truth: build a target FROM the rig at a known view and posture, then fit it back. A perfect solution
    /// exists here, so anything other than a near-zero residual is the solver failing rather than the data.
    /// </summary>
    [Fact]
    public void Fit_RecoversARigPoseFromItsOwnProjection()
    {
        var settings = Settings();
        var mannequin = PoseMannequin.Standing();

        var truth = mannequin.StandingStance();
        truth[mannequin.JointIndex("shoulder_r")] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.9f);
        truth[mannequin.JointIndex("elbow_r")] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.7f);
        truth[mannequin.JointIndex("knee_l")] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.6f);
        truth[mannequin.JointIndex("head")] = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);

        var truthView = new PoseView(YawDegrees: 30);
        var observed = PoseProjection.Project(mannequin, truth, truthView, settings);

        var fit = PoseRigFit.Fit(observed, settings);

        Assert.True(
            fit.MeanErrorPercentOfHeight < 2.0,
            $"the fit should reproduce a pose the rig itself produced, but landed at "
            + $"{fit.MeanErrorPercentOfHeight:0.00}% (worst joint {fit.WorstJoint})");
        Assert.Equal(PoseMannequin.CocoJointCount, fit.JointsMatched);
    }

    /// <summary>
    /// The fit is deterministic by construction — fixed seeds, fixed step schedule, no randomness — so the same
    /// input must give the same answer. A pose tool that reshuffled its result between runs would be unusable.
    /// </summary>
    [Fact]
    public void Fit_IsDeterministic()
    {
        var settings = Settings();
        var mannequin = PoseMannequin.Standing();
        var observed = PoseProjection.Project(
            mannequin, mannequin.StandingStance(), new PoseView(YawDegrees: -55), settings);

        var first = PoseRigFit.Fit(observed, settings);
        var second = PoseRigFit.Fit(observed, settings);

        Assert.Equal(first.MeanErrorPercentOfHeight, second.MeanErrorPercentOfHeight);
        Assert.Equal(first.View.YawDegrees, second.View.YawDegrees);
    }

    /// <summary>
    /// The real library, which is the case this exists for. The rig is a standing figure with its own proportions
    /// and a real pack pose is a photographed human of unknown build, so the residual is a number to be reported
    /// rather than a threshold to be hidden: the test records the spread across poses of different kinds and
    /// refuses only a fit that is too poor to edit from.
    /// </summary>
    [Theory]
    [InlineData("NSFW_standing/512768/NSFW_standing028.json")]
    [InlineData("NSFW_Squatting/512512/NSFW_Squatting029.json")]
    [InlineData("NSFW_Kneeling/512768/NSFW_Kneeling017.json")]
    public void Fit_LandsOnRealLibraryPosesWithinAUsableResidual(string relativePath)
    {
        var path = Path.Combine(
            RepositoryRoot(), "pose-packs", PoseLibraryIds.BundledPackFolder,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(path), $"the bundled pack file '{relativePath}' is missing");

        var observed = OpenPosePoseJson.Parse(File.ReadAllText(path), relativePath);
        var fit = PoseRigFit.Fit(observed, Settings());

        // The fitted rig must still be a usable pose: every joint present, and a residual the editor can start
        // from. 'Usable' is the tool's own published bound, so this test and the UI agree on what "good enough"
        // means instead of each inventing a number.
        Assert.Equal(PoseMannequin.CocoJointCount, fit.Reprojection.Body.Count);
        Assert.True(
            fit.JointsMatched >= 12,
            $"'{relativePath}' matched only {fit.JointsMatched} joints, which is too few to edit from");

        Assert.True(
            fit.MeanErrorPercentOfHeight < PoseRigFit.UsableMeanErrorPercentOfHeight,
            $"'{relativePath}' fitted at {fit.MeanErrorPercentOfHeight:0.00}% of height (worst joint "
            + $"{fit.WorstJoint}), above the {PoseRigFit.UsableMeanErrorPercentOfHeight:0.0}% the editor treats "
            + "as usable");

        // Records the real spread so the published bound is never mistaken for an aspiration.
        var report = Path.Combine(RepositoryRoot(), "artifacts", "tmp", "pose-probes", "rig-fit-residuals.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.AppendAllText(
            report,
            $"{relativePath}\t{fit.MeanErrorPercentOfHeight:0.00}%\tyaw {fit.View.YawDegrees:0.0}\t"
            + $"worst {fit.WorstJoint}\tmatched {fit.JointsMatched}{Environment.NewLine}");
    }

    /// <summary>
    /// The whole point of the fit: once a library pose has been fitted, projecting it must use the FITTED rotations.
    /// If the projection quietly re-derived the figure from the standing stance — which is what it did before the
    /// rotations parameter existed — the editor would show a standing figure while claiming to be editing the
    /// loaded pose, and the operator would save something they never saw.
    /// </summary>
    [Fact]
    public void ProjectingALoadedPoseUsesItsFit_NotTheStandingStance()
    {
        var settings = Settings();
        var observed = OpenPosePoseJson.Parse(
            File.ReadAllText(PackPath("NSFW_standing/512768/NSFW_standing028.json")), "pack pose");
        var fit = PoseRigFit.Fit(observed, settings);

        using var fixture = new PoseLibraryTestFixture();
        var loaded = fixture.Service.ProjectAuthoredPose(fit.View, null, fit.Rotations);
        var stance = fixture.Service.ProjectAuthoredPose(fit.View);

        // Projecting the fit reproduces the fit.
        Assert.Equal(fit.Reprojection.Body.Count, loaded.Body.Count);
        for (var index = 0; index < loaded.Body.Count; index++)
        {
            var drift = Math.Sqrt(
                Math.Pow(loaded.Body[index].X - fit.Reprojection.Body[index].X, 2)
                + Math.Pow(loaded.Body[index].Y - fit.Reprojection.Body[index].Y, 2));

            Assert.True(drift < 0.001, $"joint {index} drifted by {drift:0.###} from the fitted reprojection");
        }

        // And it is plainly not the stance, which is the failure this guards.
        var largestDifference = Enumerable.Range(0, loaded.Body.Count)
            .Max(index => Math.Sqrt(
                Math.Pow(loaded.Body[index].X - stance.Body[index].X, 2)
                + Math.Pow(loaded.Body[index].Y - stance.Body[index].Y, 2)));

        Assert.True(
            largestDifference > 1.0,
            $"the loaded projection differs from the stance projection by only {largestDifference:0.###}, so the "
            + "loaded rotations are not reaching the projection");
    }

    /// <summary>
    /// The panel re-projects on every 5° press and keeps the fit result it was handed, so the head path must not
    /// write into the caller's rotation array. Mutating it would drift the pose the panel believes it loaded and
    /// corrupt the fit it is still displaying.
    /// </summary>
    [Fact]
    public void ProjectingALoadedPoseWithAHeadTurn_LeavesTheCallersRotationsAlone()
    {
        var mannequin = PoseMannequin.Standing();
        var observed = OpenPosePoseJson.Parse(
            File.ReadAllText(PackPath("NSFW_standing/512768/NSFW_standing028.json")), "pack pose");
        var fit = PoseRigFit.Fit(observed, Settings());
        var headBefore = fit.Rotations[mannequin.HeadIndex];

        using var fixture = new PoseLibraryTestFixture();
        fixture.Service.ProjectAuthoredPose(fit.View, new PoseHeadRotation(YawDegrees: 40), fit.Rotations);

        Assert.Equal(headBefore, fit.Rotations[mannequin.HeadIndex]);
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
