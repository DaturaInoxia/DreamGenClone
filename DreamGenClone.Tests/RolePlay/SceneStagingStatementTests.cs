using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The recorded geometry is STATED, not requested. Measured 2026-10-26 (SceneImages b829fa81 / 4b627c8a): the compiler
/// input carried LEFT/RIGHT placement, both facings and the pressed-together hips, the deployed contract demanded all of
/// it, and the caption named none of it — so two renders showed Dean beside her facing the lens with a gap between them.
/// </summary>
public sealed class SceneStagingStatementTests
{
    [Fact]
    public void For_AnExternalCamera_StatesEachPlacementAndFacing()
    {
        var text = SceneStagingStatement.For(Projection(SceneImagePovFramer.Omniscient, Snapshot), Snapshot);

        Assert.StartsWith(SceneStagingStatement.Marker, text, StringComparison.Ordinal);
        // The frame's recorded placement, for both people.
        Assert.Contains("Left Foreground", text, StringComparison.Ordinal);
        Assert.Contains("Right Foreground", text, StringComparison.Ordinal);
        // The recorded world facing — the fact every dropped caption omitted.
        Assert.Contains("facing the tin wall beyond the workbench", text, StringComparison.Ordinal);
        Assert.Contains("facing the other person's back", text, StringComparison.Ordinal);
        // The pose is decomposed, not summarised: the record's own body clause is carried verbatim.
        Assert.Contains("hips raised", text, StringComparison.Ordinal);
        Assert.Contains("torso low over the workbench boards", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A contact is stated as owner → part → target → manner, so it cannot be re-attributed in prose. This is the exact
    /// clause whose loss produced two people standing apart with no relation to each other.
    /// </summary>
    [Fact]
    public void For_StatesRecordedContactsWithAnOwnerAndTarget()
    {
        var text = SceneStagingStatement.For(Projection(SceneImagePovFramer.Omniscient, Snapshot), Snapshot);

        Assert.Contains("hips pressed flat against", text, StringComparison.Ordinal);
        Assert.Contains("rear", text, StringComparison.Ordinal);
    }

    /// <summary>No name reaches the image model: people are addressed by their place in the picture.</summary>
    [Fact]
    public void For_NamesNobodyByStoryName()
    {
        var text = SceneStagingStatement.For(Projection(SceneImagePovFramer.Omniscient, Snapshot), Snapshot);

        Assert.DoesNotContain("Becky", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Dean", text, StringComparison.Ordinal);
    }

    /// <summary>A record with no contacts states none — an absent fact is omitted, never invented.</summary>
    [Fact]
    public void For_WithNoRecordedContacts_StatesNoContact()
    {
        var withoutContacts = Snapshot.Replace(
            "\"contacts\":[{\"from\":\"p1\",\"part\":\"hips\",\"to\":\"p0\",\"targetPart\":\"rear\",\"manner\":\"pressed flat against\"}]",
            "\"contacts\":[]",
            StringComparison.Ordinal);
        var text = SceneStagingStatement.For(Projection(SceneImagePovFramer.Omniscient, withoutContacts), withoutContacts);

        Assert.DoesNotContain("pressed flat against", text, StringComparison.Ordinal);
    }

    /// <summary>A User Intent draft has no projection, so there is no recorded geometry to state.</summary>
    [Fact]
    public void For_WithNoProjection_IsEmpty()
        => Assert.Empty(SceneStagingStatement.For(null, Snapshot));

    /// <summary>A camera holder is never named in their own POV, so a facing target that names them says "the camera".</summary>
    [Fact]
    public void For_AParticipantPov_ScrubsTheCameraHoldersName()
    {
        var text = SceneStagingStatement.For(Projection("Becky", SnapshotWithDeanFacingTheCamera), SnapshotWithDeanFacingTheCamera);

        Assert.Contains("the camera", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Dean", text, StringComparison.Ordinal);
    }

    /// <summary>One person in the picture is its subject, centred — never left standing in a two-shot slot.</summary>
    [Fact]
    public void For_ASingleSubject_IsCentred()
    {
        var text = SceneStagingStatement.For(Projection("Dean", Snapshot), Snapshot);

        Assert.Contains("Center, foreground", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Right Foreground", text, StringComparison.Ordinal);
    }

    private static SceneImagePovProjection.Projection Projection(string pov, string snapshot)
        => SceneImagePovProjection.Resolve(
            Brief(snapshot),
            pov,
            SceneBlockingJson.TryRead(snapshot),
            cameraHolderPartsMode: CameraHolderPartsMode.Derived);

    private static CompiledMediaBrief Brief(string snapshot) => new(
        Id: "brief-staging",
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
          "contacts":[{"from":"p1","part":"hips","to":"p0","targetPart":"rear","manner":"pressed flat against"}],
          "event":{"anchor":"workbench","line":"Becky climaxes while Dean presses against her."}},
         "blocking":{"profileVersion":"1",
          "subjects":[
            {"subjectKey":"p0","position":{"horizontal":"Left","depth":"Foreground"},"roleText":"the person on the LEFT"},
            {"subjectKey":"p1","position":{"horizontal":"Right","depth":"Foreground"},"roleText":"the person on the RIGHT"}],
          "camera":{"angle":"viewed from the side","shotSize":"wide establishing shot","side":null,"omniscientShotSize":"wide establishing shot","height":"eye"},
          "layering":["p0","p1"],"rationale":"Two figures in profile"}}
        """;

    private const string SnapshotWithDeanFacingTheCamera = """
        {"frozenState":{
          "characters":[
            {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active",
             "physicalLocation":"At the workbench","position":"Bent low over the workbench",
             "actionOrObservation":"holding quiet","sightline":"down","visibleCharacterNames":["Dean"],
             "clothing":"Nude","at":{"anchor":"workbench","posture":"hips raised"},
             "facing":{"toward":"Dean's chest"}},
            {"profileKey":"p1","characterId":"c1","name":"Dean","involvement":"active",
             "physicalLocation":"At the workbench","position":"Standing over her",
             "actionOrObservation":"Watching her","sightline":"down","visibleCharacterNames":["Becky"],
             "clothing":"Trousers open","at":{"anchor":"shed floor","posture":"standing"},
             "facing":{"toward":"Becky"}}],
          "location":"Shed","timeOfDay":"Morning","lighting":"Slat light","environment":"Shaded",
          "mood":"Clenched","objects":[],"contacts":[],
          "event":{"anchor":"workbench","line":"Becky holds quiet."}}}
        """;
}
