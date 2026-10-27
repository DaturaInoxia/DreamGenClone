using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The canonical POV projection: one brief, three POVs, three different frames. These guard the defect where the
/// Omniscient, Dean and Becky prompts described the same wide shot because the POV was only a name in the user prompt.
/// </summary>
public sealed class SceneImagePovProjectionTests
{
    [Fact]
    public void Resolve_DeanPov_PutsBeckyDirectlyInFrontAndBelowTheCamera()
    {
        var brief = BriefWithShedMoment();
        var projection = SceneImagePovProjection.Resolve(brief, "Dean", Blocking());

        Assert.False(projection.IsOmniscient);
        Assert.Equal("Standing over Becky", projection.UnseenViewpointStance);
        Assert.Equal("Directed down toward Becky at his hips", projection.UnseenViewpointSightline);
        var becky = Assert.Single(projection.InFrame);
        Assert.Equal("Becky", becky.Name);
        // The holder's name is rephrased, not dropped (debug 028).
        Assert.Equal("Kneeling before the camera", becky.Position);
        Assert.Equal(new[] { "Becky" }, projection.DirectlyAhead);
        Assert.Empty(projection.OutOfFrame);

        var text = projection.ToPromptText();
        Assert.Contains("first-person point of view", text, StringComparison.OrdinalIgnoreCase);
        // A standing holder framing a kneeling subject is ABOVE them — the opposite of the Becky POV of the same moment.
        Assert.Contains("Camera height: above the subject — the shot looks down on them", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Standing over Becky", text, StringComparison.Ordinal);
        Assert.Contains("Camera aim: downward", text, StringComparison.Ordinal);
        Assert.Contains("In frame (this is the whole cast", text, StringComparison.Ordinal);
        // Stated AFFIRMATIVELY: the old negative wording came back as picture content ("likely looking up at the person
        // holding the camera (not visible)", record eabff5a2).
        Assert.Contains("Describe only the people listed above", text, StringComparison.Ordinal);
        Assert.DoesNotContain("person holding the camera", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Out of frame", text, StringComparison.Ordinal);
        // The in-frame subject's facts name the camera holder, so they face the lens.
        Assert.Contains("FACING — this person faces the camera: show their face.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_DeanPov_StatesFacingAwayWhenTheSubjectFacesTheBoards()
    {
        // The "straight-on view" defect: Becky's facts put her face on the boards (away from Dean), but the
        // pre-processor rephrased that into "a straight-on view" and the image model turned her around. The SHOT block
        // must state the deterministic conclusion so it cannot be rephrased away.
        var brief = BriefWithShedMoment(BentOverFacingAwayJson());
        var text = SceneImagePovProjection.Resolve(brief, "Dean", Blocking()).ToPromptText();

        Assert.Contains("FACING — this person faces AWAY from the camera: show their BACK, never their face.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_DeanPov_StatesNoFacingWhenTheBriefRecordsNone()
    {
        // A brief whose subject facts name neither the camera holder nor a surface/direction records no facing; the
        // prompt must not invent one (no-guess).
        var brief = BriefWithShedMoment(NoFacingRecordedJson());
        var text = SceneImagePovProjection.Resolve(brief, "Dean", Blocking()).ToPromptText();

        Assert.DoesNotContain("FACING —", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_V4FacingTarget_IsReadExactlyRatherThanPatternMatched()
    {
        // B-167: `facing.toward` names the person the body faces. Naming the viewpoint's profile key means "faces the
        // camera" with no prose scan at all; naming anything else means the back is toward this lens.
        var brief = BriefWithShedMoment(V4FacingJson(subjectToward: "p1", viewpointToward: "p0"));

        var deanText = SceneImagePovProjection.Resolve(brief, "Dean", Blocking()).ToPromptText();
        Assert.Contains("FACING — this person faces the camera: show their face.", deanText, StringComparison.Ordinal);

        var beckyText = SceneImagePovProjection.Resolve(brief, "Becky", Blocking()).ToPromptText();
        Assert.Contains("FACING — this person faces the camera: show their face.", beckyText, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_V4FacingTarget_NamesTheCharacterDirectlyAhead()
    {
        // The holder's own facing target is the exact answer to "who is in front of this camera"; the v3 path had to
        // substring-match a name out of a sightline sentence.
        var brief = BriefWithShedMoment(V4FacingJson(subjectToward: "p1", viewpointToward: "p0"));

        var projection = SceneImagePovProjection.Resolve(brief, "Dean", Blocking());

        Assert.Equal(["Becky"], projection.DirectlyAhead);
    }

    [Fact]
    public void Resolve_V4FacingTarget_WrappedInAPreposition_ResolvesTowardTheCamera()
    {
        // Live 2026-10-27 (prompt record 8655924e): the enrichment recorded `facing.toward` as "toward p0" instead of
        // the bare key. Compared verbatim it matched nothing, so the geometry was inverted to "faces AWAY", and the
        // phrase itself was printed into the frame block, where the caption writer copied "p0" into the image prompt.
        var brief = BriefWithShedMoment(V4FacingJson(subjectToward: "toward p1", viewpointToward: "toward p0"));

        var projection = SceneImagePovProjection.Resolve(brief, "Becky", Blocking());
        var dean = Assert.Single(projection.InFrame);

        Assert.Equal(SceneImagePovProjection.SubjectFacing.TowardCamera, dean.Facing!.Value);
        Assert.Equal("toward the camera", dean.WorldFacing);

        var text = projection.ToPromptText();
        Assert.Contains("FACING — this person faces the camera: show their face.", text, StringComparison.Ordinal);
        Assert.Contains("facing: \"toward the camera\"", text, StringComparison.Ordinal);
        // A profile key is never a word of the prompt, and the camera holder is never named: they are the camera.
        Assert.DoesNotContain("p0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("p1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Becky", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WorldBody_NeverNamesTheCameraHolder()
    {
        // The v4 world body ("posture at anchor") is recorded in world terms and can name the holder ("standing over
        // Becky"). It was the one fact `ToProjected` did not anonymise, so it reached the compiler verbatim and the
        // caption named the person who is the camera (live 2026-10-27, prompt record 8655924e).
        var brief = BriefWithShedMoment(V4FacingJson(
            subjectToward: "p1",
            viewpointToward: "toward p0",
            viewpointPosture: "standing over Becky"));

        var text = SceneImagePovProjection.Resolve(brief, "Becky", Blocking()).ToPromptText();

        Assert.Contains("body: \"standing over the camera at workbench\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Becky", text, StringComparison.Ordinal);
        Assert.DoesNotContain("p0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("p1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DropViewpoint_RemovesTheEventAndContactsThatNameTheCameraHolder()
    {
        // The selected event line and the recorded contacts are the facts the caption now reads, so they obey the same
        // rule the whole-event prose did: the camera holder is not in their own picture.
        const string snapshot = """
            {"frozenState":{
              "characters":[{"profileKey":"p0","name":"Becky"},{"profileKey":"p1","name":"Dean"}],
              "event":{"anchor":"workbench","line":"Dean bends Becky over the workbench."},
              "contacts":[{"from":"p1","part":"hands","to":"p0","targetPart":"hips","manner":"grip"}]}}
            """;

        var scrubbed = System.Text.Json.Nodes.JsonNode
            .Parse(SceneImagePovProjection.DropViewpoint(snapshot, "p1"))!.AsObject();

        var eventNode = scrubbed["frozenState"]!["event"]!.AsObject();
        // The sentence described the camera holder, so it is dropped rather than handed to the caption writer.
        Assert.Null(eventNode["line"]);
        // The camera holder's own contact is not a fact about the pictured cast anymore.
        Assert.Empty(scrubbed["frozenState"]!["contacts"]!.AsArray());
    }

    private static string V4FacingJson(
        string subjectToward,
        string viewpointToward,
        string subjectPosture = "kneeling",
        string viewpointPosture = "standing") => $$$"""
        {"frozenState":{"visualDescription":"Inside the shed, two people close","characters":[
          {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active","physicalLocation":"Inside the shed","position":"Kneeling before Dean","actionOrObservation":"Bracing on the boards","sightline":"Directed up toward Dean","visibleCharacterNames":["Dean"],"clothing":"Nude",
           "at":{"anchor":"boards","posture":"{{{subjectPosture}}}"},"facing":{"toward":"{{{subjectToward}}}"}},
          {"profileKey":"p1","characterId":"c1","name":"Dean","involvement":"active","physicalLocation":"Inside the shed","position":"Standing over Becky","actionOrObservation":"Holding the workbench edge","sightline":"Directed down toward Becky","visibleCharacterNames":["Becky"],"clothing":"clothed",
           "at":{"anchor":"workbench","posture":"{{{viewpointPosture}}}"},"facing":{"toward":"{{{viewpointToward}}}"}}],
          "location":"Shed behind the trailers","timeOfDay":"Morning","lighting":"Barred slat light","environment":"Grit on the boards","mood":"Charged","objects":["workbench"],
          "event":{"anchor":"boards","line":"Becky kneels before Dean."},"contacts":[]}}
        """;

    [Fact]
    public void Resolve_CameraHolderParts_ComeFromTheRecordedContacts()
    {
        // B-167: a contact is a fact ("from p1's hands to p0's hips"), so the holder's visible parts are a lookup.
        // The holder is p1 (Dean), so his own `part` values are what can appear in his first-person frame.
        var brief = BriefWithShedMoment("""
            {"frozenState":{"visualDescription":"Inside the shed","characters":[
              {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active","physicalLocation":"Inside the shed","position":"Kneeling before Dean","actionOrObservation":"Bracing","sightline":"Directed up toward Dean","visibleCharacterNames":["Dean"],"clothing":"Nude"},
              {"profileKey":"p1","characterId":"c1","name":"Dean","involvement":"active","physicalLocation":"Inside the shed","position":"Standing over Becky","actionOrObservation":"Holding her hair","sightline":"Directed down toward Becky","visibleCharacterNames":["Becky"],"clothing":"clothed"}],
              "location":"Shed behind the trailers","timeOfDay":"Morning","lighting":"Barred slat light","environment":"Grit on the boards","mood":"Charged","objects":["workbench"],"continuityState":"stable",
              "contacts":[{"from":"p1","part":"hands","to":"p0","targetPart":"hips","manner":"grip"}]}}
            """);

        var projection = SceneImagePovProjection.Resolve(
            brief, "Dean", Blocking(),
            cameraHolderPartsMode: CameraHolderPartsMode.Derived);

        Assert.Equal("the camera holder's hands on Becky's hips (grip); the camera holder's wardrobe: clothed", projection.CameraHolderParts);
        var text = projection.ToPromptText();
        Assert.Contains("CAMERA HOLDER'S VISIBLE BODY", text, StringComparison.Ordinal);
        Assert.Contains("the camera holder's hands on Becky's hips (grip); the camera holder's wardrobe: clothed", text, StringComparison.Ordinal);
        // The note becomes prompt text: it names people, never their profile key ("(p1)" is weight syntax in ComfyUI).
        Assert.DoesNotContain("p0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("p1", text, StringComparison.Ordinal);
        Assert.Contains("part of the caption, not an addendum", text, StringComparison.Ordinal);
        Assert.DoesNotContain("block appended AFTER your caption", text, StringComparison.Ordinal);
        Assert.Contains("never", text, StringComparison.OrdinalIgnoreCase);

        var rule = SceneImagePovProjection.PovSystemRule(projection);
        Assert.Contains("PART OF THE CAPTION, NOT AN ADDENDUM", rule, StringComparison.Ordinal);
        Assert.Contains("behind the camera", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_CameraHolderParts_AreNullWhenTheRecordCarriesNoContacts()
    {
        // A v3 record has no contacts: "not recorded", so nothing is invented and no parts block is emitted.
        var projection = SceneImagePovProjection.Resolve(
            BriefWithShedMoment(), "Dean", Blocking(),
            cameraHolderPartsMode: CameraHolderPartsMode.Derived);

        Assert.Null(projection.CameraHolderParts);
        Assert.DoesNotContain("CAMERA HOLDER'S VISIBLE BODY", projection.ToPromptText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_CameraHolderParts_DeriveFromASelfContactWithoutNamingTheHolderTwice()
    {
        // A SELF-contact (both ends are the holder) is one statement about their own body. Emitting both ends would
        // describe the same limb twice, and naming the holder twice reads to a caption model as a second person.
        var brief = BriefWithShedMoment("""
            {"frozenState":{"visualDescription":"Inside the shed","characters":[
              {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active","physicalLocation":"Inside the shed","position":"Kneeling","actionOrObservation":"Bracing","sightline":"Directed up toward Dean","visibleCharacterNames":["Dean"],"clothing":"Nude"},
              {"profileKey":"p1","characterId":"c1","name":"Dean","involvement":"active","physicalLocation":"Inside the shed","position":"Standing over Becky","actionOrObservation":"Watching her","sightline":"Directed down toward Becky","visibleCharacterNames":["Becky"],"clothing":"clothed"}],
              "location":"Shed behind the trailers","timeOfDay":"Morning","lighting":"Barred slat light","environment":"Grit on the boards","mood":"Charged","objects":["workbench"],"continuityState":"stable",
              "contacts":[{"from":"p1","part":"hand","to":"p1","targetPart":"thigh","manner":"press"}]}}
            """);

        var projection = SceneImagePovProjection.Resolve(
            brief, "Dean", Blocking(),
            cameraHolderPartsMode: CameraHolderPartsMode.Derived);

        Assert.Equal("hand (press own thigh); the camera holder's wardrobe: clothed", projection.CameraHolderParts);
        Assert.DoesNotContain("p1", projection.CameraHolderParts!, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RetiredCustomCameraHolderParts_IsRefusedByName()
    {
        var error = Assert.Throws<InvalidOperationException>(() => SceneImagePovProjection.Resolve(
            BriefWithShedMoment(), "Dean", Blocking(),
            cameraHolderPartsMode: CameraHolderPartsMode.Custom,
            cameraHolderPartsText: "his hands on her hips"));

        Assert.Contains("retired", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contacts", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without parts the projection must not claim the holder's body at all — neither the shot block that hands it to the
    /// writer, nor the system rule that requires it in the caption.
    /// </summary>
    [Fact]
    public void Resolve_NoneCameraHolderParts_EmitsNoPartsLine()
    {
        var projection = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Dean", Blocking());

        Assert.Null(projection.CameraHolderParts);
        Assert.DoesNotContain("CAMERA HOLDER'S VISIBLE BODY (part of the caption", projection.ToPromptText(), StringComparison.Ordinal);
        Assert.DoesNotContain("— PART OF THE CAPTION, NOT AN ADDENDUM", SceneImagePovProjection.PovSystemRule(projection), StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_CustomCameraHolderPartsWithoutText_FailsFast()
    {
        Assert.Throws<InvalidOperationException>(() => SceneImagePovProjection.Resolve(
            BriefWithShedMoment(), "Dean", Blocking(),
            cameraHolderPartsMode: CameraHolderPartsMode.Custom,
            cameraHolderPartsText: null));
    }

    [Fact]
    public void Resolve_BeckyPov_SeesDeanStandingOverHer()
    {
        var projection = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Becky", Blocking());

        Assert.Equal("Kneeling before Dean", projection.UnseenViewpointStance);
        var dean = Assert.Single(projection.InFrame);
        Assert.Equal("Dean", dean.Name);
        Assert.Equal("Standing over the camera", dean.Position);
        Assert.Equal(new[] { "Dean" }, projection.DirectlyAhead);

        // The two participant POVs are different frames, not one frame with a different name.
        var deanPovText = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Dean", Blocking()).ToPromptText();
        Assert.NotEqual(deanPovText, projection.ToPromptText());
        Assert.Contains("Standing over the camera", projection.ToPromptText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_Omniscient_StatesEachPersonsExactFramePlacement()
    {
        var projection = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Omniscient", Blocking());

        Assert.True(projection.IsOmniscient);
        Assert.Equal(new[] { "Becky", "Dean" }, projection.InFrame.Select(character => character.Name));

        var text = projection.ToPromptText();
        Assert.Contains("external third-person camera", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Becky — CENTER, in the foreground, nearest the camera", text, StringComparison.Ordinal);
        Assert.Contains("Dean — LEFT, in the foreground, nearest the camera", text, StringComparison.Ordinal);
        Assert.Contains("LEFT / CENTER / RIGHT", text, StringComparison.Ordinal);
        Assert.Contains("low angle", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_OmniscientWithoutBlocking_DoesNotInventAPlacement()
    {
        var projection = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Omniscient", blocking: null);

        var text = projection.ToPromptText();
        Assert.Contains("not recorded on this brief", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CENTER", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_UnknownPov_FailsExplicitly()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Absent", Blocking()));

        Assert.Contains("not associated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_VisibleNameMissingFromCast_FailsExplicitly()
    {
        var brief = BriefWithShedMoment() with
        {
            SemanticInputSnapshotJson = FrozenStateJson().Replace("\"visibleCharacterNames\":[\"Dean\"]", "\"visibleCharacterNames\":[\"Stranger\"]", StringComparison.Ordinal)
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => SceneImagePovProjection.Resolve(brief, "Becky", Blocking()));

        Assert.Contains("Stranger", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_BriefWithoutFrozenState_FailsExplicitly()
    {
        var brief = BriefWithShedMoment() with { SemanticInputSnapshotJson = """{"lineage":{}}""" };

        var exception = Assert.Throws<InvalidOperationException>(
            () => SceneImagePovProjection.Resolve(brief, "Dean", Blocking()));

        Assert.Contains("frozenState", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RemoteObserver_StaysOutOfAParticipantFrame()
    {
        var brief = BriefWithShedMoment() with
        {
            SemanticInputSnapshotJson = """
                {"frozenState":{"visualDescription":"Becky kneels before Dean in the shed while Ken watches through the window","characters":[
                  {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active","physicalLocation":"shed interior","position":"Kneeling before Dean","actionOrObservation":"her mouth on him","sightline":"Directed forward and up toward Dean's hips","visibleCharacterNames":["Dean"],"clothing":"Nude"},
                  {"profileKey":"p1","characterId":"c1","name":"Dean","involvement":"active","physicalLocation":"shed interior","position":"Standing over Becky","actionOrObservation":"his fist in her hair","sightline":"Directed down toward Becky","visibleCharacterNames":["Becky"],"clothing":"clothed"},
                  {"profileKey":"p2","characterId":"c2","name":"Ken","involvement":"observer","physicalLocation":"yard outside the window","position":"standing at the window, behind Dean","actionOrObservation":"watching through the glass","sightline":"forward through the window","visibleCharacterNames":["Becky","Dean"],"clothing":"shorts"}],
                  "location":"shed interior","timeOfDay":"morning","lighting":"slatted light","environment":"grit and boards","mood":"charged","objects":["window"],"continuityState":"stable"}}
                """
        };

        var projection = SceneImagePovProjection.Resolve(brief, "Dean", blocking: null);

        Assert.DoesNotContain(projection.InFrame, character => character.Name == "Ken");
        Assert.Contains("Ken", projection.OutOfFrame);
        Assert.Contains("Ken", projection.ToPromptText(), StringComparison.Ordinal);

        // An observer's own POV keeps the camera where that observer is recorded standing.
        var observerPov = SceneImagePovProjection.Resolve(brief, "Ken", blocking: null);
        Assert.Equal("yard outside the window", observerPov.UnseenViewpointLocation);
        Assert.Contains("Camera is at: yard outside the window", observerPov.ToPromptText(), StringComparison.Ordinal);
        Assert.Contains("REMOTE OBSERVER", observerPov.ToPromptText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A bound location image removes <c>frozenState.location</c> (and environment/lighting) from the prompt payload by
    /// design. The frame is projected from the COMPILED brief, so the projection still works and simply stops restating
    /// the place — the live failure was prompt generation refusing outright once a location was bound (2026-10-20).
    /// </summary>
    [Fact]
    public void Resolve_WithTheLocationRemovedFromThePrompt_StillProjectsTheFrame()
    {
        var suppression = new ScenePromptOverrides
        {
            Fields =
            [
                new ScenePromptFieldOverride { ElementKey = "frozenState.location", Removed = true },
                new ScenePromptFieldOverride { ElementKey = "frozenState.environment", Removed = true },
                new ScenePromptFieldOverride { ElementKey = "frozenState.lighting", Removed = true }
            ]
        };

        var projection = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Dean", Blocking(), suppression);

        Assert.Equal("Standing over Becky", projection.UnseenViewpointStance);
        Assert.True(projection.SuppressSceneLocation);
        var text = projection.ToPromptText();
        Assert.DoesNotContain("Scene location:", text, StringComparison.Ordinal);
        Assert.Contains("Kneeling before the camera", text, StringComparison.Ordinal);
    }

    /// <summary>A bound pose supplies the stance, so the projection must not restate it in prose.</summary>
    [Fact]
    public void Resolve_WithTheViewpointsStanceSuppliedByAPoseReference_DoesNotRestateIt()
    {
        var suppression = new ScenePromptOverrides
        {
            Fields =
            [
                new ScenePromptFieldOverride { ElementKey = "character:p1.position", Removed = true }
            ]
        };

        var projection = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Dean", Blocking(), suppression);

        Assert.Null(projection.UnseenViewpointStance);
        Assert.Equal("Directed down toward Becky at his hips", projection.UnseenViewpointSightline);
        var text = projection.ToPromptText();
        Assert.DoesNotContain("Camera height:", text, StringComparison.Ordinal);
        Assert.Contains("Camera aim:", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A character the operator removed is not depicted — and the survivor is CENTRED, not left standing in the removed
    /// person's two-shot slot: a picture with one person in it has no "to the left of" (operator 2026-10-25: "why left,
    /// only one person, default should be center").
    /// </summary>
    [Fact]
    public void Resolve_WithARemovedCharacter_DropsThemFromTheFrameAndCentresTheSurvivor()
    {
        var suppression = new ScenePromptOverrides { RemovedCharacters = ["Becky"] };

        var projection = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Omniscient", Blocking(), suppression);

        Assert.Equal(new[] { "Dean" }, projection.InFrame.Select(character => character.Name));
        var text = projection.ToPromptText();
        Assert.DoesNotContain("Becky — CENTER", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Dean — LEFT", text, StringComparison.Ordinal);
        Assert.Contains("Dean — Center Foreground, the only person in the picture.", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// B-166 D11: a participant POV is NOT always eye-level and NOT always close. When the brief records a pitch and a
    /// subject distance they are stated; when it records neither, the projection says so instead of assuming a default.
    /// </summary>
    [Fact]
    public void Resolve_ParticipantPov_StatesTheRecordedPitchAndSubjectDistance()
    {
        var projection = SceneImagePovProjection.Resolve(
            BriefWithShedMoment(FrozenStateJsonWithViewpointCamera()), "Dean", Blocking());

        Assert.Equal("Down", projection.ViewpointPitch);
        Assert.Equal("Far", projection.ViewpointSubjectDistance);
        var text = projection.ToPromptText();
        // A participant POV's angle is its OWN geometry. The compiled camera angle was authored for the scene's frame and
        // made Dean's POV read "low angle" while its own aim said "from above" (record 4da992ba, 2026-10-22).
        Assert.Contains("high angle, viewed from above", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Subject distance: Far", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not recorded on this brief", text, StringComparison.Ordinal);

        // What the OPERATOR picked still wins: that is an override, not an inherited frame.
        var operatorFramed = SceneImagePovProjection.Resolve(
            BriefWithShedMoment(FrozenStateJsonWithViewpointCamera()), "Dean", Blocking(),
            operatorCamera: new SceneBlockingCameraOverride("medium shot", "eye level"));
        Assert.Contains("eye level", operatorFramed.ToPromptText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// B-166 D11: with no explicit pitch field, the vertical aim is READ from the recorded sightline — "directed down"
    /// is a high angle from above, "directed up" a low angle from below — so the shot states the vantage instead of
    /// defaulting to eye level. Nothing is assumed when neither is recorded.
    /// </summary>
    [Fact]
    public void Resolve_ParticipantPov_DerivesTheAngleFromTheRecordedSightline()
    {
        var projection = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Dean", Blocking("eye level"));
        var text = projection.ToPromptText();
        Assert.Contains("high angle, viewed from above", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("first-person point of view", text, StringComparison.Ordinal);
        // The camera holder is never given a noun the model would have to interpret.
        Assert.DoesNotContain("viewpoint", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// B-166 D5: when a location image owns the room, the caption still has to say what KIND of place it is, derived
    /// mechanically from the brief's own location text. The room's structure is never described.
    /// </summary>
    [Fact]
    public void Resolve_WithAReferenceSuppliedRoom_NamesTheSettingKindOnly()
    {
        var projection = SceneImagePovProjection.Resolve(
            BriefWithShedMoment(), "Omniscient", Blocking(), suppression: null, sceneSuppliedByReference: true);

        // The KIND is the head noun phrase: "Shed behind the trailers - gritty boards" names a SHED. Carrying the whole
        // label made the caption model pad around it with its own room guess ("within a small, confined space that gives
        // an intimate feeling", record eabff5a2).
        Assert.Equal("Shed", projection.LocationType);
        var text = projection.ToPromptText();
        Assert.Contains("\"Shed\"", text, StringComparison.Ordinal);
        Assert.Contains("name it only as that kind of place", text, StringComparison.Ordinal);
        Assert.Contains("never its structure", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// B-166 POV fix: the camera holder is not a subject. Their entry, their continuity states and their name in the
    /// other cast members' visible lists must all be gone from the snapshot, or the compiler depicts them anyway.
    /// </summary>
    [Fact]
    public void DropViewpoint_RemovesTheCameraHolderFromEveryDepiction()
    {
        var dropped = SceneImagePovProjection.DropViewpoint(FrozenStateJson(), "p1");

        // No per-character entry survives: the projection states the in-frame cast, and a second copy in the brief is
        // where the camera holder's name came back ("Standing over Becky", measured 2026-10-21).
        Assert.DoesNotContain("\"characters\"", dropped, StringComparison.Ordinal);
        Assert.DoesNotContain("Becky", dropped, StringComparison.Ordinal);
        Assert.DoesNotContain("Dean", dropped, StringComparison.Ordinal);
        // The scene-level anchors that name nobody stay; MOOD does not, because a caption model pads a bare setting with
        // it and invented a room out of "intimate" (record eabff5a2, 2026-10-22).
        Assert.Contains("\"timeOfDay\"", dropped, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mood\"", dropped, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole-event prose is the leak the model turned into a second person ("Becky works him orally … as Dean grips
    /// her hair"). For a participant POV it is removed, because the projection already states what is in frame.
    /// </summary>
    [Fact]
    public void DropViewpoint_RemovesWholeEventProseAndTheTypedReferencesToTheCameraHolder()
    {
        var dropped = SceneImagePovProjection.DropViewpoint(FrozenStateJson(), "p1");

        Assert.DoesNotContain("\"visibleAction\"", dropped, StringComparison.Ordinal);
        Assert.DoesNotContain("\"visualDescription\"", dropped, StringComparison.Ordinal);
        Assert.DoesNotContain("\"participantSummary\"", dropped, StringComparison.Ordinal);
    }

    /// <summary>
    /// The real brief carries a SECOND copy of the moment as whole-event prose under <c>moment.frozenState</c>, plus
    /// continuity states written from another character's angle ("facing Becky"). Both name the camera holder, and the
    /// compiler turned them into a depicted second person ("the kneeling viewpoint character … her hair … she lies nude
    /// beneath him", measured on brief dd2d8c8f for the Becky POV with no references bound).
    /// </summary>
    [Fact]
    public void DropViewpoint_RemovesTheMomentProseAndContinuityStatesThatNameTheCameraHolder()
    {
        const string brief = """
            {"moment":{"momentId":"m2","frozenState":"Inside the shed, Becky kneels on gritty boards, nude, her mouth on him; Dean stands clothed above her, his fist closed in her hair"},
             "frozenState":{"characters":[
               {"profileKey":"p0","name":"Becky","position":"Kneeling before Dean","actionOrObservation":"Kneeling nude"},
               {"profileKey":"p1","name":"Dean","position":"Standing over Becky","actionOrObservation":"Standing clothed above Becky"}],
               "location":"Shed behind the trailers","lighting":"Barred slat light"},
             "continuity":{
               "start":{"characterStates":[{"key":"p1","value":"Standing inside the shed, clothed, facing Becky."}],"lighting":"dim"},
               "end":{"characterStates":[{"key":"p1","value":"Hauling Becky up by the arm, clothed, his fist having been in her hair."}]}}}
            """;

        var dropped = SceneImagePovProjection.DropViewpoint(brief, "p0");

        Assert.DoesNotContain("Becky", dropped, StringComparison.Ordinal);
        Assert.DoesNotContain("\"frozenState\":\"", dropped, StringComparison.Ordinal);
        Assert.Contains("\"characterStates\":[]", dropped, StringComparison.Ordinal);
        // Everything that does not name her is untouched.
        Assert.Contains("Shed behind the trailers", dropped, StringComparison.Ordinal);
        Assert.Contains("Barred slat light", dropped, StringComparison.Ordinal);
        Assert.Contains("\"lighting\":\"dim\"", dropped, StringComparison.Ordinal);
    }

    /// <summary>
    /// A participant who simply stands somewhere described differently from the scene's own phrase is NOT a remote
    /// observer. Comparing the two strings said "outside the scene" for someone standing in the middle of it and told
    /// the model to move the camera away (measured live 2026-10-20).
    /// </summary>
    [Fact]
    public void Resolve_ParticipantInTheScene_IsNotDescribedAsARemoteObserver()
    {
        var projection = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Dean", Blocking());

        Assert.False(projection.UnseenViewpointOutsideScene);
        Assert.DoesNotContain("REMOTE OBSERVER", projection.ToPromptText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The exact defect reported on 2026-10-21: the caption was written as first-person NARRATION ("In front of me…
    /// his fist clenched in my hair") instead of a photograph description, which no image model can render. And the
    /// camera-holder's own body was reworded into "the unseen viewpoint's hair" — a noun that means nothing.
    /// </summary>
    [Fact]
    public void Resolve_ParticipantPov_WritesAShotNotANarration_AndOmitsTheCameraHoldersBody()
    {
        var projection = SceneImagePovProjection.Resolve(
            BriefWithShedMoment(), "Becky", Blocking("eye level"));
        var text = projection.ToPromptText();

        // A camera line the model family understands, stated first.
        Assert.StartsWith("SHOT (authoritative frame", text, StringComparison.Ordinal);
        Assert.Contains("Camera: medium shot, low angle, viewed from below, first-person point of view", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("third person", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unseen viewpoint", text, StringComparison.OrdinalIgnoreCase);

        // Dean's own action mentioned Becky's hair; that clause is not renderable from her eyes and is dropped.
        var dean = Assert.Single(projection.InFrame);
        Assert.Equal("Standing clothed above the camera", dean.ActionOrObservation);
        Assert.DoesNotContain("hair", dean.ActionOrObservation!, StringComparison.OrdinalIgnoreCase);
        // The spatial facts that ARE renderable survive.
        Assert.Equal("Standing over the camera", dean.Position);
    }

    /// <summary>
    /// A per-character row the operator rewrote must reach the frame the compiler is given. The compiler never sees the
    /// raw cast for a participant POV (DropViewpoint removes it), so the projection is the ONLY carrier of "what Dean is
    /// doing" — reading the un-edited enrichment silently threw the operator's text away (reported 2026-10-22).
    /// </summary>
    [Fact]
    public void Resolve_UsesTheOperatorsEditedCharacterFacts()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields =
            [
                new ScenePromptFieldOverride
                {
                    ElementKey = "character:p1.actionOrObservation",
                    Value = "Standing with his pants undone, fly wide open"
                }
            ]
        };

        var projection = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Becky", Blocking("eye level"), overrides);

        var dean = Assert.Single(projection.InFrame);
        Assert.Equal("Standing with his pants undone, fly wide open", dean.ActionOrObservation);
    }

    /// <summary>
    /// Record 5365dfe5 (2026-10-22): the camera-height line read "at the eye height of a person "Kneeling before Dean"" and
    /// the model wrote "Dean stands clothed above a kneeling person's eye level". The height is a CAMERA fact and must
    /// never be expressed as somebody's eyes, and the recorded position must contribute only the room.
    /// </summary>
    [Fact]
    public void Resolve_ParticipantPov_StatesTheCameraHeightRelativeToTheSubjectAndOnlyTheRoom()
    {
        var text = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Becky", Blocking()).ToPromptText();

        Assert.Contains("Camera height: low — below the subject's waist", text, StringComparison.Ordinal);
        Assert.Contains("Camera is at: Inside the shed", text, StringComparison.Ordinal);
        // Nothing in the frame block may describe a second body at the camera.
        Assert.DoesNotContain("eye height of a person", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Kneeling before Dean", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A sightline that targets the camera holder's BODY collapses to its direction: after the name scrub its possessive
    /// dangles ("directed down toward the camera at HIS hips") and the model rendered it as "directed downward at their
    /// hips" — a second person again.
    /// </summary>
    [Fact]
    public void Resolve_ParticipantPov_ReducesASightlineOntoTheCameraHolderToItsDirection()
    {
        var projection = SceneImagePovProjection.Resolve(BriefWithShedMoment(), "Becky", Blocking());

        var dean = Assert.Single(projection.InFrame);
        Assert.Equal("Directed down", dean.Sightline);
        Assert.DoesNotContain("his hips", projection.ToPromptText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Operator prose that records one body against another is dropped even when it never names the holder: "her mouth
    /// on him" has no name, but at a POV it can only be rendered by inventing the person it belongs to.
    /// </summary>
    [Fact]
    public void DropViewpoint_DropsOperatorSegmentsThatDescribeABodyInContact()
    {
        const string brief = """
            {"moment":{"momentId":"m2","frozenState":"Inside the shed, her mouth on him, Dean stands clothed, his pants undone with the fly wide open, his fist closed in her hair"},
             "frozenState":{"characters":[{"profileKey":"p0","name":"Becky"},{"profileKey":"p1","name":"Dean"}],"location":"Shed behind the trailers"}}
            """;

        var edited = SceneImagePovProjection.DropViewpoint(
            brief, "p0", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "moment.frozenState" });

        Assert.DoesNotContain("her mouth", edited, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("her hair", edited, StringComparison.OrdinalIgnoreCase);
        // The wardrobe and the spatial relation are what the operator was adding; they survive.
        Assert.Contains("Dean stands clothed", edited, StringComparison.Ordinal);
        Assert.Contains("his pants undone with the fly wide open", edited, StringComparison.Ordinal);
        Assert.Contains("Inside the shed", edited, StringComparison.Ordinal);
    }

    /// <summary>
    /// A member the OPERATOR rewrote keeps the sentences that do not name the camera holder, and loses the ones that do.
    /// </summary>
    /// <remarks>
    /// A literal name substitution was tried first and made the prompt WORSE (record 4438d917): "Becky kneels nude …
    /// her mouth is sucking his dick" became "the camera kneels nude … her mouth is sucking his dick", and the model
    /// turned "the camera" into a second depicted person ("the kneeling nude figure"). A camera cannot kneel, so the
    /// sentence goes — and the operator's other sentence, the detail they were actually adding, stays verbatim.
    /// </remarks>
    [Fact]
    public void DropViewpoint_KeepsOnlyTheOperatorsSentencesThatDoNotNameTheCameraHolder()
    {
        const string brief = """
            {"moment":{"momentId":"m2","frozenState":"Inside the shed, Becky kneels on gritty boards, her mouth on him; Dean stands clothed above her, his pants undone with the fly wide open"},
             "frozenState":{"characters":[{"profileKey":"p0","name":"Becky"},{"profileKey":"p1","name":"Dean"}],"location":"Shed behind the trailers"}}
            """;

        var edited = SceneImagePovProjection.DropViewpoint(
            brief, "p0", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "moment.frozenState" });

        Assert.DoesNotContain("Becky", edited, StringComparison.Ordinal);
        Assert.DoesNotContain("the camera kneels", edited, StringComparison.Ordinal);
        Assert.DoesNotContain("her mouth on him", edited, StringComparison.Ordinal);
        // The detail the operator was adding survives untouched.
        Assert.Contains("Dean stands clothed above her, his pants undone with the fly wide open", edited, StringComparison.Ordinal);
    }

    /// <summary>
    /// An edited member whose every sentence names the camera holder is removed outright: nothing renderable is left.
    /// </summary>
    /// <remarks>
    /// The rule is name-based on purpose. Matching pronouns ("she", "her") would delete "Dean stands clothed above her,
    /// his pants undone with the fly wide open" — the operator's own sentence, which uses "her" to mean the camera
    /// holder and is exactly the detail they were adding.
    /// </remarks>
    [Fact]
    public void DropViewpoint_RemovesAnEditedMemberWhenEverySentenceNamesTheCameraHolder()
    {
        const string brief = """
            {"moment":{"momentId":"m2","frozenState":"Becky kneels on gritty boards. Becky is nude."},
             "frozenState":{"characters":[{"profileKey":"p0","name":"Becky"}],"location":"Shed behind the trailers"}}
            """;

        var edited = SceneImagePovProjection.DropViewpoint(
            brief, "p0", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "moment.frozenState" });

        Assert.DoesNotContain("Becky", edited, StringComparison.Ordinal);
        Assert.DoesNotContain("\"frozenState\":\"", edited, StringComparison.Ordinal);
    }

    /// <summary>Without an operator edit the same member is still removed outright — the POV scrub is unchanged.</summary>
    [Fact]
    public void DropViewpoint_StillRemovesAnUneditedMemberThatNamesTheCameraHolder()
    {
        const string brief = """
            {"moment":{"momentId":"m2","frozenState":"Inside the shed, Becky kneels on gritty boards"},
             "frozenState":{"characters":[{"profileKey":"p0","name":"Becky"}],"location":"Shed behind the trailers"}}
            """;

        var dropped = SceneImagePovProjection.DropViewpoint(brief, "p0");

        Assert.DoesNotContain("Becky", dropped, StringComparison.Ordinal);
        Assert.DoesNotContain("\"frozenState\":\"", dropped, StringComparison.Ordinal);
    }

    /// <summary>
    /// The operator's chosen HEIGHT outranks the stance-derived one, and carries the angle that height implies. This is
    /// the one control that says "put the lens here", where the stances can only say where the bodies put it.
    /// </summary>
    [Fact]
    public void Resolve_AnOperatorCameraHeightOutranksTheStanceDerivedOne()
    {
        var projection = SceneImagePovProjection.Resolve(
            BriefWithShedMoment(), "Becky", Blocking("eye level"),
            operatorCamera: new SceneBlockingCameraOverride("medium shot", null, CameraHeights.Overhead));

        var text = projection.ToPromptText();

        Assert.Contains("Camera height: bird's eye perspective, looking straight down", text, StringComparison.Ordinal);
        // The height states the aim, so the first camera line cannot still read as a low angle from below.
        Assert.Contains("Camera: medium shot, high angle, viewed from above, first-person point of view", text, StringComparison.Ordinal);
        Assert.DoesNotContain("low angle", text, StringComparison.OrdinalIgnoreCase);
    }

    private static CompiledMediaBrief BriefWithShedMoment() => BriefWithShedMoment(FrozenStateJson());

    /// <summary>
    /// Two people are recorded but only ONE is inside the scene: the other is a remote observer, and an Omniscient
    /// still names them as out of frame. The visible person must then hold the CENTRE seat, not the left one the
    /// compiled two-person frame gave her (SceneImage 3bbad8c9 reported the LEFT case, 2026-10-23).
    /// </summary>
    [Fact]
    public void Resolve_OmniscientGivesTheLoneVisiblePersonTheCentreSeat()
    {
        var brief = BriefWithShedMoment("""
            {"frozenState":{"visualDescription":"Becky bends over the workbench while Ken watches from the yard","characters":[
              {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active","physicalLocation":"Shed behind the trailers","position":"Bent over the workbench","actionOrObservation":"Bracing on the boards","sightline":"Down at the boards","visibleCharacterNames":["Ken"],"clothing":"Nude"},
              {"profileKey":"p1","characterId":"c1","name":"Ken","involvement":"observer","physicalLocation":"yard outside the window","position":"At the window","actionOrObservation":"watching through the glass","sightline":"forward through the window","visibleCharacterNames":["Becky"],"clothing":"shorts"}],
              "location":"Shed behind the trailers","timeOfDay":"Morning","lighting":"Barred slat light","environment":"Grit on the boards","mood":"Charged","objects":["workbench"],"continuityState":"stable"}}
            """);
        var twoSeatBlocking = new SceneBlocking(
            "1",
            [
                new SceneBlockingSubject("p0", new FramePosition(FrameHorizontal.Left, FrameDepth.Foreground), "the person on the LEFT in the foreground"),
                new SceneBlockingSubject("p1", new FramePosition(FrameHorizontal.Right, FrameDepth.Foreground), "the person on the RIGHT in the foreground")
            ],
            new SceneBlockingCamera("eye level", "medium shot", null),
            ["p0", "p1"],
            "Two figures at the bench");

        var projection = SceneImagePovProjection.Resolve(brief, SceneImagePovFramer.Omniscient, twoSeatBlocking);

        var subject = Assert.Single(projection.Frame!.Subjects);
        Assert.Equal("p0", subject.SubjectKey);
        Assert.Equal(FrameHorizontal.Center, subject.Position.Horizontal);
        Assert.Contains("CENTER", subject.RoleText, StringComparison.Ordinal);

        // The excluded person's NAME must never be printed, not even as "not in the active scene": a caption writer
        // echoes it and the contract then refuses both the name and the out-of-frame statement (live 2026-10-27).
        var text = projection.ToPromptText();
        Assert.DoesNotContain("Ken", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Not in the active scene", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// An "observer" is a narrative role, not a spatial one. Ken stands at the kitchen counter in the same open trailer
    /// as Becky on the couch, so an Omniscient frame shows both — the compiled blocking already seats him in the
    /// background. Reading the involvement as "outside the scene" dropped him from the projection while the frame still
    /// named him, and the caption failed on both the name and an out-of-frame statement (live 2026-10-27, record
    /// 1e4a0666).
    /// </summary>
    [Fact]
    public void Resolve_Omniscient_KeepsAnObserverWhoStandsInTheSameRoom()
    {
        var brief = BriefWithShedMoment("""
            {"frozenState":{"visualDescription":"Becky sits on the couch while Ken stands at the kitchen counter","characters":[
              {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active","physicalLocation":"on the living-room couch","position":"Sitting on the couch","actionOrObservation":"Shifts her legs against the fabric","sightline":"Forward toward Ken","visibleCharacterNames":["Ken"],"clothing":"Casual clothes","at":{"anchor":"couch","posture":"sitting"},"facing":{"toward":"p1"}},
              {"profileKey":"p1","characterId":"c1","name":"Ken","involvement":"observer","physicalLocation":"by the kitchen counter","position":"Standing by the counter a few feet from Becky across the open floor","actionOrObservation":"Looks down at the counter","sightline":"Down at the counter","visibleCharacterNames":["Becky"],"clothing":"a t-shirt","at":{"anchor":"counter","posture":"standing"},"facing":{"toward":"p0"}}],
              "location":"Husband and Wife Trailer — Shared Private Space - the living room couch","timeOfDay":"afternoon","lighting":"warm light","environment":"open-plan interior","mood":"quiet","objects":["couch"],"continuityState":"stable"}}
            """);
        var twoSeatBlocking = new SceneBlocking(
            "1",
            [
                new SceneBlockingSubject("p0", new FramePosition(FrameHorizontal.Left, FrameDepth.Foreground), "the person on the LEFT in the foreground"),
                new SceneBlockingSubject("p1", new FramePosition(FrameHorizontal.Right, FrameDepth.Background), "the person on the RIGHT in the background")
            ],
            new SceneBlockingCamera("eye level", "wide establishing shot", null),
            ["p0", "p1"],
            "Two figures in one open room");

        var projection = SceneImagePovProjection.Resolve(brief, SceneImagePovFramer.Omniscient, twoSeatBlocking);

        Assert.Equal(["Becky", "Ken"], projection.InFrame.Select(character => character.Name));
        Assert.Empty(projection.OutOfFrame);
        Assert.Equal(2, projection.Frame!.Subjects.Count);
        var text = projection.ToPromptText();
        Assert.Contains("Ken —", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Not in the active scene", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A participant POV has no external frame at all, so no reference may be named "the person on the LEFT"; an
    /// Omniscient still keeps only the people inside the scene, invited by the same one decision path.
    /// </summary>
    [Fact]
    public void BlockingForFrame_WithholdsTheExternalFrameFromAParticipantPov()
    {
        var brief = BriefWithShedMoment();

        Assert.Null(SceneImagePovProjection.BlockingForFrame(brief, "Dean", Blocking()));
        Assert.NotNull(SceneImagePovProjection.BlockingForFrame(brief, SceneImagePovFramer.Omniscient, Blocking()));
    }

    private static CompiledMediaBrief BriefWithShedMoment(string frozenStateJson) => new(
        Id: "brief-shed",
        MediaKind: MediaProductionKind.StillImage,
        TargetProfileId: "canonical-still",
        TargetProfileVersion: "2",
        FamilyKey: "canonical",
        CompilerKey: "deterministic",
        CompilerVersion: "1",
        ProviderRequestContractVersion: "canonical-request-v1",
        Lineage: new CompiledMediaLineage("cat1", "beat1", "plan1", 1, "momset1", 1, "m2", "enr1", 1),
        CanonicalSourceIds: ["src1"],
        SemanticInputSnapshotJson: frozenStateJson,
        ProviderRequestSnapshotJson: """{"contract":"canonical-request-v1"}""",
        RequiredIntentCoverageJson: """{"entries":[]}""",
        Status: MediaCompilerStatus.Complete,
        ErrorCode: null,
        ErrorMessage: null,
        CreatedUtc: DateTime.UtcNow.AddMinutes(-1),
        CompletedUtc: DateTime.UtcNow);

    private static string FrozenStateJson() => """
        {"frozenState":{"visualDescription":"Inside the shed, Becky kneels nude on gritty boards, her mouth on Dean; Dean stands clothed above her with his fist in her hair","characters":[
          {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active","physicalLocation":"Inside the shed, on the gritty boards","position":"Kneeling before Dean","actionOrObservation":"Kneeling nude with both hands braced on Dean's hips","sightline":"Directed forward and up toward Dean's hips and torso","visibleCharacterNames":["Dean"],"clothing":"Nude"},
          {"profileKey":"p1","characterId":"c1","name":"Dean","involvement":"active","physicalLocation":"Inside the shed, standing above Becky","position":"Standing over Becky","actionOrObservation":"Standing clothed above Becky, his fist closed in her hair","sightline":"Directed down toward Becky at his hips","visibleCharacterNames":["Becky"],"clothing":"Rugged/Casual clothing"}],
          "location":"Shed behind the trailers","timeOfDay":"Morning","lighting":"Barred slat light","environment":"Grit on the boards and a workbench","mood":"Charged","objects":["workbench"],"continuityState":"stable"}}
        """;

    /// <summary>
    /// The same moment with the camera holder's pitch and subject distance RECORDED (B-166 D11).
    /// </summary>
    private static string FrozenStateJsonWithViewpointCamera() => """
        {"frozenState":{"visualDescription":"Inside the shed, Becky kneels nude on gritty boards, her mouth on Dean; Dean stands clothed above her with his fist in her hair","characters":[
          {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active","physicalLocation":"Inside the shed, on the gritty boards","position":"Kneeling before Dean","actionOrObservation":"Kneeling nude with both hands braced on Dean's hips","sightline":"Directed forward and up toward Dean's hips and torso","visibleCharacterNames":["Dean"],"clothing":"Nude"},
          {"profileKey":"p1","characterId":"c1","name":"Dean","involvement":"active","physicalLocation":"Inside the shed, standing above Becky","position":"Standing over Becky","actionOrObservation":"Standing clothed above Becky, his fist closed in her hair","sightline":"Directed down toward Becky at his hips","visibleCharacterNames":["Becky"],"clothing":"Rugged/Casual clothing","viewpointPitch":"Down","viewpointSubjectDistance":"Far"}],
          "location":"Shed behind the trailers","timeOfDay":"Morning","lighting":"Barred slat light","environment":"Grit on the boards and a workbench","mood":"Charged","objects":["workbench"],"continuityState":"stable"}}
        """;

    /// <summary>
    /// Becky bent over the workbench, facing the boards and away from Dean behind her — the "from behind" orientation.
    /// </summary>
    private static string BentOverFacingAwayJson() => """
        {"frozenState":{"visualDescription":"Becky is bent over the workbench, facing the boards, with Dean standing close behind her","characters":[
          {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active","physicalLocation":"At the workbench","position":"Bent over the workbench directly in front of Dean, facing forward toward the boards","actionOrObservation":"Bent forward with her palms flat on the boards","sightline":"Down at the gritty workbench boards beneath her palms","visibleCharacterNames":["Dean"],"clothing":"Nude"},
          {"profileKey":"p1","characterId":"c1","name":"Dean","involvement":"active","physicalLocation":"Directly behind Becky","position":"Standing directly behind Becky","actionOrObservation":"Standing close behind Becky","sightline":"Down at Becky's back and her ass","visibleCharacterNames":["Becky"],"clothing":"Rugged/Casual clothing"}],
          "location":"Shed behind the trailers","timeOfDay":"Morning","lighting":"Barred slat light","environment":"Grit on the boards and a workbench","mood":"Charged","objects":["workbench"],"continuityState":"stable"}}
        """;

    /// <summary>A brief whose subject facts record no facing signal at all.</summary>
    private static string NoFacingRecordedJson() => """
        {"frozenState":{"visualDescription":"Two people near the workbench","characters":[
          {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active","physicalLocation":"Near the workbench","position":"Beside the workbench","actionOrObservation":"Holding position","sightline":"Across the room","visibleCharacterNames":["Dean"],"clothing":"Nude"},
          {"profileKey":"p1","characterId":"c1","name":"Dean","involvement":"active","physicalLocation":"Near the workbench","position":"Standing nearby","actionOrObservation":"Standing nearby","sightline":"Toward the bench","visibleCharacterNames":["Becky"],"clothing":"Rugged/Casual clothing"}],
          "location":"Shed behind the trailers","timeOfDay":"Morning","lighting":"Barred slat light","environment":"Grit on the boards and a workbench","mood":"Charged","objects":["workbench"],"continuityState":"stable"}}
        """;

    private static SceneBlocking Blocking(string angle = "low angle") => new(
        "1",
        [
            new SceneBlockingSubject("p0", new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground), "the person in the CENTER in the foreground"),
            new SceneBlockingSubject("p1", new FramePosition(FrameHorizontal.Left, FrameDepth.Foreground), "the person on the LEFT in the foreground")
        ],
        new SceneBlockingCamera(angle, "medium shot", null),
        ["p0", "p1"],
        "Low interior angle");
}
