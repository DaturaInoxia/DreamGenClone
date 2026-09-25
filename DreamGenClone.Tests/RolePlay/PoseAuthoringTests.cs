using System.Numerics;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using SixLabors.ImageSharp;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The authoring path is the first place the projection is written down as a pose, so these pin what actually
/// reaches the library and what is refused on the way there.
/// </summary>
public sealed class PoseAuthoringTests
{
    private static readonly PoseView ThreeQuarterRight = new(YawDegrees: 45);

    private const int NoseIndex = 0;
    private const int RightShoulderIndex = 2;
    private const int LeftShoulderIndex = 5;
    private const int RightHipIndex = 8;
    private const int RightAnkleIndex = 10;

    [Fact]
    public void ProjectAuthoredPose_ProducesAFullBodyForTheRig()
    {
        using var fixture = new PoseLibraryTestFixture();

        var person = fixture.Service.ProjectAuthoredPose(ThreeQuarterRight);

        Assert.Equal(PoseMannequin.CocoJointCount, person.Body.Count);
        Assert.All(person.Body, point => Assert.Equal(1.0, point.Confidence));
    }

    [Fact]
    public void RenderAuthoredPreview_ProducesARenderablePng()
    {
        using var fixture = new PoseLibraryTestFixture();

        var bytes = fixture.Service.RenderAuthoredPreview(ThreeQuarterRight, head: null, 512);

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], bytes.Take(4).ToArray());
        using var image = Image.Load(bytes);
        Assert.Equal(512, image.Width);
    }

    [Fact]
    public async Task SaveAuthoredPose_WritesThePoseItsSkeletonAndItsRecipe()
    {
        using var fixture = new PoseLibraryTestFixture();

        var preset = await SaveAsync(fixture, "Standing 3/4 right");

        Assert.Equal("Standing 3/4 right", preset.Name);
        Assert.Equal("standing", preset.Category);
        Assert.Equal(PoseLibraryIds.Authored, preset.LibraryId);

        // A fresh pose has not been measured, so it must not claim known-good.
        Assert.False(preset.KnownGood);

        var keypoints = OpenPosePoseJson.Parse(preset.KeypointsJson, "saved pose");
        Assert.Equal(PoseMannequin.CocoJointCount, keypoints.Body.Count);

        Assert.NotNull(preset.SkeletonPngPath);
        var skeleton = Path.Combine(
            fixture.WebRoot, BodyStanceSkeletons.WebRootFolder, preset.SkeletonPngPath!);
        Assert.True(File.Exists(skeleton), $"the skeleton should exist at '{skeleton}'");

        // The recipe is stored, so the pose can be re-opened and turned again rather than being a one-way result.
        Assert.NotNull(preset.ProvenanceJson);
        Assert.Contains("mannequin", preset.ProvenanceJson!, StringComparison.Ordinal);
        Assert.Contains("45", preset.ProvenanceJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAuthoredPose_IsSearchableImmediately()
    {
        using var fixture = new PoseLibraryTestFixture();
        await SaveAsync(fixture, "Reaching out");

        var hits = await fixture.Service.SearchAsync(new PoseLibraryQuery("reaching"));
        var inLibrary = await fixture.Service.SearchAsync(
            new PoseLibraryQuery(null, null, PoseLibraryIds.Authored));

        Assert.Single(hits);
        Assert.Single(inLibrary);
        Assert.Equal("Reaching out", hits[0].Name);
    }

    [Fact]
    public async Task SaveAuthoredPose_WithADuplicateName_IsRefusedRatherThanOverwritten()
    {
        using var fixture = new PoseLibraryTestFixture();
        await SaveAsync(fixture, "Standing 3/4 right");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaveAsync(fixture, "standing 3/4 right"));

        Assert.Contains("already holds a pose named", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAuthoredPose_IntoAPack_IsRefused()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.WritePose("NSFW_standing/512768/NSFW_standing028.json");
        await fixture.Importer.ImportAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.SaveAuthoredPoseAsync(new AuthoredPoseRequest(
                "Into a pack", "standing", null, ThreeQuarterRight, PoseLibraryIds.BundledPackFolder)));

        Assert.Contains("rebuilt on every import", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAuthoredPose_WithoutTheRequiredFields_IsRefusedByName()
    {
        using var fixture = new PoseLibraryTestFixture();
        await fixture.Service.EnsureAuthoredLibraryAsync();

        var noName = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.SaveAuthoredPoseAsync(new AuthoredPoseRequest(
                "  ", "standing", null, ThreeQuarterRight, PoseLibraryIds.Authored)));
        Assert.Contains("needs a name", noName.Message, StringComparison.Ordinal);

        var noCategory = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.SaveAuthoredPoseAsync(new AuthoredPoseRequest(
                "A pose", "", null, ThreeQuarterRight, PoseLibraryIds.Authored)));
        Assert.Contains("needs a category", noCategory.Message, StringComparison.Ordinal);

        var noLibrary = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.SaveAuthoredPoseAsync(new AuthoredPoseRequest(
                "A pose", "standing", null, ThreeQuarterRight, "no-such-library")));
        Assert.Contains("does not exist", noLibrary.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureAuthoredLibrary_CreatesItOnceAndFindsItAfterwards()
    {
        using var fixture = new PoseLibraryTestFixture();

        var first = await fixture.Service.EnsureAuthoredLibraryAsync();
        var second = await fixture.Service.EnsureAuthoredLibraryAsync();

        Assert.Equal(PoseLibraryIds.Authored, first.Id);
        Assert.Equal(PoseLibraryIds.AuthoredName, first.Name);
        Assert.False(first.IsSystem);
        Assert.Equal(first.Id, second.Id);
        Assert.Single((await fixture.Service.ListLibrariesAsync()).Where(library => library.Id == first.Id));
    }

    [Fact]
    public void ProjectAuthoredPose_WithAHeadTurn_MovesTheHeadAndNotTheBody()
    {
        using var fixture = new PoseLibraryTestFixture();

        var rest = fixture.Service.ProjectAuthoredPose(new PoseView(), head: null);
        var turned = fixture.Service.ProjectAuthoredPose(
            new PoseView(), new PoseHeadRotation(YawDegrees: 90));

        // The neck is the pivot, so the body is untouched and only the head moves.
        foreach (var index in new[] { RightShoulderIndex, LeftShoulderIndex, RightHipIndex, RightAnkleIndex })
        {
            Assert.Equal(rest.Body[index].X, turned.Body[index].X, 6);
            Assert.Equal(rest.Body[index].Y, turned.Body[index].Y, 6);
        }

        Assert.NotEqual(rest.Body[NoseIndex].X, turned.Body[NoseIndex].X, 3);
    }

    [Fact]
    public void ProjectAuthoredPose_HeadYawNarrowsTheEyeSeparation()
    {
        using var fixture = new PoseLibraryTestFixture();

        var front = fixture.Service.ProjectAuthoredPose(new PoseView(), new PoseHeadRotation());
        var profile = fixture.Service.ProjectAuthoredPose(
            new PoseView(), new PoseHeadRotation(YawDegrees: 90));

        var frontEyes = Math.Abs(front.Body[15].X - front.Body[14].X);
        var profileEyes = Math.Abs(profile.Body[15].X - profile.Body[14].X);

        Assert.True(profileEyes < frontEyes * 0.2,
            $"a head profile should bring the eyes together, but {frontEyes:0.###} became {profileEyes:0.###}");
    }

    [Fact]
    public void HeadRotation_PositivePitchTipsTheHeadBack()
    {
        // The convention is checked on the rotation itself, where perspective cannot muddy it. A positive pitch
        // carries the nose up and back relative to a negative pitch, and at 45° it is unambiguously behind its rest
        // position. Note it is NOT true that a downward pitch puts the nose further forward than rest: the rest
        // offset already leans forward, so z peaks at a small rotation and falls away on either side of it.
        var rest = new Vector3(0f, 0.02f, 0.09f);

        var up = Vector3.Transform(rest, new PoseHeadRotation(PitchDegrees: 45).ToLocalRotation());
        var down = Vector3.Transform(rest, new PoseHeadRotation(PitchDegrees: -45).ToLocalRotation());

        Assert.True(up.Y > down.Y,
            $"a positive pitch should raise the nose relative to a negative one, but up={up} and down={down}");
        Assert.True(up.Z < down.Z,
            $"a positive pitch should tip the head back relative to a negative one, but up={up} and down={down}");
        Assert.True(up.Z < rest.Z, $"at 45° the nose should be behind its rest position, but it went to {up}");
        Assert.True(down.Y < rest.Y, $"a negative pitch should drop the nose, but it went to {down}");
    }

    [Fact]
    public void ProjectAuthoredPose_HeadPitchChangesTheProjectionAndLeavesTheBodyAlone()
    {
        using var fixture = new PoseLibraryTestFixture();

        var level = fixture.Service.ProjectAuthoredPose(new PoseView(), new PoseHeadRotation());
        var up = fixture.Service.ProjectAuthoredPose(new PoseView(), new PoseHeadRotation(PitchDegrees: 45));

        Assert.NotEqual(level.Body[NoseIndex].Y, up.Body[NoseIndex].Y, 3);

        // The nose moves; the shoulders do not, because the head has its own pivot above the neck.
        Assert.Equal(level.Body[RightShoulderIndex].Y, up.Body[RightShoulderIndex].Y, 6);
        Assert.Equal(level.Body[RightShoulderIndex].X, up.Body[RightShoulderIndex].X, 6);
    }

    [Fact]
    public void RenderAuthoredHeadPreview_FramesOnTheHeadRatherThanTheWholeFigure()
    {
        using var fixture = new PoseLibraryTestFixture();

        var bytes = fixture.Service.RenderAuthoredHeadPreview(
            new PoseView(), new PoseHeadRotation(YawDegrees: 45), 320);

        using var image = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(bytes);
        Assert.Equal(320, image.Width);

        // The point of a head framing: the head has to fill a real share of the frame. Body-framing the same pose
        // would leave it at a few percent, which is unusable for judging an angle.
        var rowsWithInk = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                if (pixel.R != 0 || pixel.G != 0 || pixel.B != 0)
                {
                    rowsWithInk++;
                    break;
                }
            }
        }

        Assert.True(rowsWithInk > image.Height / 4,
            $"the head should occupy a useful share of a head-framed render but covers only {rowsWithInk}/{image.Height} rows");
    }

    [Fact]
    public async Task SaveAuthoredPose_RecordsTheHeadRotationInTheRecipe()
    {
        using var fixture = new PoseLibraryTestFixture();
        await fixture.Service.EnsureAuthoredLibraryAsync();

        var preset = await fixture.Service.SaveAuthoredPoseAsync(new AuthoredPoseRequest(
            "Looking up", "portrait", null, new PoseView(), PoseLibraryIds.Authored,
            new PoseHeadRotation(PitchDegrees: 60)));

        Assert.NotNull(preset.ProvenanceJson);
        Assert.Contains("\"head\"", preset.ProvenanceJson!, StringComparison.Ordinal);
        Assert.Contains("60", preset.ProvenanceJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAuthoredPose_WithNoHeadRotation_OmitsTheHeadFromTheRecipe()
    {
        // A neutral head is an absence, not a fabricated zero rotation.
        using var fixture = new PoseLibraryTestFixture();
        var preset = await SaveAsync(fixture, "Neutral head");

        Assert.DoesNotContain("\"head\"", preset.ProvenanceJson!, StringComparison.Ordinal);
    }

    [Fact]
    public void HeadRotation_PitchIsClampedShortOfThePole()
    {
        var head = PoseHeadRotation.Rotate(new PoseHeadRotation(), PoseRotationAxis.Pitch, 400);

        Assert.Equal(PoseHeadRotation.PitchLimitDegrees, head.PitchDegrees, 6);
    }

    [Fact]
    public void HeadRotation_StepUsesTheConfiguredSize()
    {
        var settings = Settings();

        var stepped = PoseHeadRotation.Step(new PoseHeadRotation(), PoseRotationAxis.Yaw, 1, settings);

        Assert.Equal(5, stepped.YawDegrees, 6);
    }

    [Fact]
    public void HeadRotation_NeutralIsReportedAsNeutral()
    {
        Assert.True(new PoseHeadRotation().IsNeutral);
        Assert.False(new PoseHeadRotation(YawDegrees: 5).IsNeutral);
    }

    private static PoseStudioOptions Settings(
        double? focal = 1600, double? distance = 4.5, int? canvas = 1024, double? step = 5) => new()
        {
            FocalLengthPx = focal,
            CameraDistance = distance,
            Canvas = canvas,
            RotationStepDegrees = step
        };

    private static async Task<PosePreset> SaveAsync(PoseLibraryTestFixture fixture, string name)
    {
        // The authored library has to exist before a pose can go into it; the tool creates it on open, so the
        // test does the same rather than relying on a hidden creation inside save.
        await fixture.Service.EnsureAuthoredLibraryAsync();

        return await fixture.Service.SaveAuthoredPoseAsync(new AuthoredPoseRequest(
            name, "standing", "turn", ThreeQuarterRight, PoseLibraryIds.Authored));
    }
}
