using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The camera holder's own visible parts are placed as DATA at render time, and the frame's inclusion is scoped to the
/// SUBJECT. Measured 2026-10-27 (SceneImage 516cc462, Dean POV with Camera Holder parts on): the caption named his hands
/// and hips, the prompt also demanded "every person in frame is in frame from crown to feet", and the render produced two
/// disembodied hands growing out of the subject's hips. A camera holder is the CAMERA: their parts enter from the frame's
/// near edge, and the rest of them does not exist in the picture.
/// </summary>
public sealed class CameraHolderBodyStatementTests
{
    [Fact]
    public void For_AParticipantPovWithParts_PlacesThemAtTheCamera()
    {
        var text = CameraHolderBodyStatement.For(Projection("Dean", Snapshot));

        Assert.StartsWith(CameraHolderBodyStatement.Marker, text, StringComparison.Ordinal);
        // The recorded parts, from the contacts rather than from the caption.
        Assert.Contains("hips", text, StringComparison.Ordinal);
        Assert.Contains("hands", text, StringComparison.Ordinal);
        // The documented POV cues (Qwen Image Edit / Kontext guide, "change first-person view showing hands"): the view
        // must be NAMED as first-person, and the hands must enter from the bottom edge in the near foreground, or the
        // model renders a third person whose hands happen to be near her hips.
        Assert.Contains("FIRST-PERSON POV", text, StringComparison.Ordinal);
        Assert.Contains("BOTTOM EDGE", text, StringComparison.Ordinal);
        Assert.Contains("NEAR FOREGROUND", text, StringComparison.Ordinal);
        // The geometry that was missing: they come from the CAMERA, not from the subject's arms.
        Assert.Contains("never out of the subject's own arms", text, StringComparison.Ordinal);
        Assert.Contains("no head", text, StringComparison.Ordinal);
        // Wardrobe rides in the same note; it is not a body part and must not be listed as one.
        Assert.DoesNotContain("wardrobe", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An external frame is not first-person, so nothing of the holder is placed.</summary>
    [Fact]
    public void For_AnOmniscientPov_IsEmpty()
        => Assert.Empty(CameraHolderBodyStatement.For(Projection(SceneImagePovFramer.Omniscient, Snapshot)));

    /// <summary>A participant POV that shows none of the holder states nothing — an absent fact is omitted.</summary>
    [Fact]
    public void For_AParticipantPovWithNoRecordedParts_IsEmpty()
        => Assert.Empty(CameraHolderBodyStatement.For(
            Projection("Dean", Snapshot, CameraHolderPartsMode.None)));

    [Fact]
    public void For_WithNoProjection_IsEmpty()
        => Assert.Empty(CameraHolderBodyStatement.For(null));

    /// <summary>
    /// "Every person in frame is whole and uncropped" is true of an external frame and impossible for the camera: the
    /// participant sentence keeps the SUBJECT whole and exempts the holder.
    /// </summary>
    [Fact]
    public void ParticipantInclusion_ScopesTheWholeBodyRuleToTheSubject()
    {
        var text = FrameInclusion.ParticipantSentence("hands and hips");

        Assert.Contains("FIRST-PERSON POV", text, StringComparison.Ordinal);
        Assert.Contains("the subject is in frame from crown to feet", text, StringComparison.Ordinal);
        Assert.Contains("only their hands and hips are in the picture", text, StringComparison.Ordinal);
        Assert.Contains("bottom edge", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Every person in frame", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The camera line states the default inclusion for every external render and the participant one only when asked —
    /// this is the assertion that the Omniscient prompt is untouched.
    /// </summary>
    [Fact]
    public void CameraStatement_StatesTheExternalInclusionByDefault()
    {
        var blocking = new SceneBlocking(
            "frame",
            [],
            new SceneBlockingCamera("eye level perspective", "medium shot", null),
            [],
            string.Empty);

        var external = SceneCameraStatement.For(SceneImageModelFamily.QwenImage21, blocking);
        Assert.Contains(FrameInclusion.Sentence, external, StringComparison.Ordinal);

        var participant = SceneCameraStatement.For(
            SceneImageModelFamily.QwenImage21, blocking, FrameInclusion.ParticipantSentence("hands and hips"));
        Assert.Contains("not a second person", participant, StringComparison.Ordinal);
        Assert.DoesNotContain(FrameInclusion.Sentence, participant, StringComparison.Ordinal);
    }

    /// <summary>
    /// The compile-time SHOT block follows the same rule: the writer is told the holder is the camera when their parts
    /// are in the frame, and told nothing new when they are not.
    /// </summary>
    [Fact]
    public void ProjectionText_ScopesTheInclusionOnlyForAParticipantWithParts()
    {
        var participant = Projection("Dean", Snapshot).ToPromptText();
        Assert.Contains("The camera holder is the camera, not a second person", participant, StringComparison.Ordinal);
        Assert.DoesNotContain("Every person in frame is in frame", participant, StringComparison.Ordinal);

        var omniscient = Projection(SceneImagePovFramer.Omniscient, Snapshot).ToPromptText();
        Assert.DoesNotContain("not a second person", omniscient, StringComparison.Ordinal);
    }

    /// <summary>Both note shapes this app produces, with wardrobe excluded and no duplicate parts.</summary>
    [Fact]
    public void PartNamesOf_ReadsBothNoteShapesAndSkipsWardrobe()
    {
        var parts = CameraHolderPartsFromContacts.PartNamesOf(
            "the camera holder's hips on Becky's rear (pressed flat against); "
            + "the camera holder's hands on Becky's thighs (clenching hard); "
            + "the camera holder's wardrobe: trousers open");

        Assert.Equal(new[] { "hips", "hands" }, parts);
    }

    [Fact]
    public void PartNamesOf_ReadsASelfContactPart()
        => Assert.Equal(
            new[] { "hand" },
            CameraHolderPartsFromContacts.PartNamesOf(
                "hand (pressed against own thigh); the camera holder's wardrobe: clothed"));

    /// <summary>
    /// A standing holder framing a subject BENT OVER the bench is ABOVE them, so the frame looks down. "bent over" used
    /// to classify as STAND (the word "over"), which collapsed the geometry into "the subject's own eye height" — the one
    /// angle in which the holder's hands have nowhere to enter the picture from. Measured 2026-10-27: the same scene
    /// rendered correctly only when the operator forced Height=overhead, and the two renders without it came back with
    /// the hands growing out of the subject's hips.
    /// </summary>
    [Fact]
    public void Projection_DerivesADownwardCameraForAStandingHolderOverABentOverSubject()
    {
        var projection = Projection("Dean", Snapshot);

        Assert.Equal(CameraHeights.Above, SceneImagePovProjection.CameraHeightKey(projection));

        var text = projection.ToPromptText();
        Assert.Contains("Camera height: above the subject — the shot looks down on them", text, StringComparison.Ordinal);
        Assert.Contains("Camera aim: downward", text, StringComparison.Ordinal);
        Assert.Contains("high angle, viewed from above", text, StringComparison.Ordinal);
    }

    /// <summary>The operator's own framing pick still outranks the derived one.</summary>
    [Fact]
    public void CameraHeightKey_KeepsTheOperatorsOwnPickOnTop()
    {
        var projection = Projection(
            "Dean",
            Snapshot,
            operatorCamera: new SceneBlockingCameraOverride("close shot", null, CameraHeights.Overhead));

        Assert.Equal(CameraHeights.Overhead, SceneImagePovProjection.CameraHeightKey(projection));
    }

    private static SceneImagePovProjection.Projection Projection(
        string pov,
        string snapshot,
        CameraHolderPartsMode mode = CameraHolderPartsMode.Derived,
        SceneBlockingCameraOverride? operatorCamera = null)
        => SceneImagePovProjection.Resolve(
            Brief(snapshot),
            pov,
            SceneBlockingJson.TryRead(snapshot),
            cameraHolderPartsMode: mode,
            operatorCamera: operatorCamera);

    private static CompiledMediaBrief Brief(string snapshot) => new(
        Id: "brief-holder-body",
        MediaKind: MediaProductionKind.StillImage,
        TargetProfileId: "canonical-still",
        TargetProfileVersion: "3",
        FamilyKey: "canonical",
        CompilerKey: "deterministic",
        CompilerVersion: "1",
        ProviderRequestContractVersion: "canonical-request-v1",
        Lineage: new CompiledMediaLineage("cat1", "beat1", "plan1", 1, "momset1", 1, "m2", "enr1", 3),
        CanonicalSourceIds: ["src1"],
        SemanticInputSnapshotJson: snapshot,
        ProviderRequestSnapshotJson: """{"contract":"canonical-request-v1"}""",
        RequiredIntentCoverageJson: """{"entries":[]}""",
        Status: MediaCompilerStatus.Complete,
        ErrorCode: null,
        ErrorMessage: null,
        CreatedUtc: DateTime.UtcNow.AddMinutes(-1),
        CompletedUtc: DateTime.UtcNow);

    private const string Snapshot = """
        {"frozenState":{
          "characters":[
            {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active",
             "physicalLocation":"At the workbench inside the shed","position":"Bent low over the workbench",
             "actionOrObservation":"holding quiet through climax","sightline":"down",
             "visibleCharacterNames":[],"clothing":"Nude",
             "at":{"anchor":"workbench","posture":"torso low over the workbench boards, hips raised, feet on the shed floor"},
             "facing":{"toward":"the tin wall beyond the workbench"}},
            {"profileKey":"p1","characterId":"c1","name":"Dean","involvement":"active",
             "physicalLocation":"Behind the workbench in the shed","position":"Standing directly behind her",
             "actionOrObservation":"Remains pressed against her","sightline":"down toward her back",
             "visibleCharacterNames":["Becky"],"clothing":"Rugged casual clothing, trousers open at the waist",
             "at":{"anchor":"shed floor","posture":"standing with feet planted, torso close over her back"},
             "facing":{"toward":"Becky's back"}}],
          "location":"Shed behind the trailers","timeOfDay":"Morning","lighting":"Bars of slat light",
          "environment":"Shaded interior","mood":"Clenched","objects":["workbench"],
          "contacts":[
            {"from":"p1","part":"hips","to":"p0","targetPart":"rear","manner":"pressed flat against"},
            {"from":"p1","part":"hands","to":"p0","targetPart":"thighs","manner":"clenching hard"}],
          "event":{"anchor":"workbench","line":"Becky climaxes while Dean presses against her."}},
         "blocking":{"profileVersion":"1",
          "subjects":[
            {"subjectKey":"p0","position":{"horizontal":"Left","depth":"Foreground"},"roleText":"the person on the LEFT"},
            {"subjectKey":"p1","position":{"horizontal":"Right","depth":"Foreground"},"roleText":"the person on the RIGHT"}],
          "camera":{"angle":"viewed from the side","shotSize":"wide establishing shot","side":null,"omniscientShotSize":"wide establishing shot","height":"eye"},
          "layering":["p0","p1"],"rationale":"Two figures in profile"}}
        """;
}
