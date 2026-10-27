using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The POV projection is the ONLY carrier of a participant frame's facts, so it must read the facts the operator
/// actually typed. Two live defects from 2026-10-25 (SceneImage 665618db) are pinned here: a contact the operator had
/// replaced still reached the camera-holder note, and the holder's name-substitution left a sentence a caption writer
/// could invert.
/// </summary>
public sealed class SceneImagePovProjectionOperatorEditsTests
{
    private const string DeanPosition =
        "Dean is standing directly behind Becky with his hips pressed to her rear and his torso close over her back, "
        + "hands clenching her thighs, facing forward toward her back and the workbench";

    private const string BeckyPosition =
        "Becky is bent over at the waist with her torso low over the workbench, directly in front of Dean and facing "
        + "away from him toward the tin wall; her hips are raised at about Dean's waist height, her head and forearm are "
        + "lower over the bench, and her body is pressed tightly against Dean's hips";

    /// <summary>
    /// The operator replaced the contacts with "hands on her thighs". The camera holder's visible body is derived from
    /// the contacts, so the note must be the hands — never the enrichment's original "penis buried to the root".
    /// </summary>
    [Fact]
    public void Resolve_CameraHolderPartsHonourAnOperatorContactEdit()
    {
        var brief = Brief();

        var unedited = SceneImagePovProjection.Resolve(
            brief, "Dean", Blocking(), cameraHolderPartsMode: CameraHolderPartsMode.Derived);
        Assert.Equal("the camera holder's penis on Becky's vagina (buried to the root); the camera holder's wardrobe: Trousers open", unedited.CameraHolderParts);

        var edited = SceneImagePovProjection.Resolve(
            brief, "Dean", Blocking(),
            suppression: ContactOverride("p1|hands|p0|thighs|clenching tightly"),
            cameraHolderPartsMode: CameraHolderPartsMode.Derived);

        Assert.Equal("the camera holder's hands on Becky's thighs (clenching tightly); the camera holder's wardrobe: Trousers open", edited.CameraHolderParts);
        Assert.DoesNotContain("buried to the root", edited.ToPromptText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_CameraHolderPartsAreUnchangedWhenTheOperatorEditsSomethingElse()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields =
            [
                new ScenePromptFieldOverride
                {
                    ElementKey = "character:faee1ec0-1cf3-459e-97d2-ad59717c41ba.position",
                    Value = DeanPosition
                }
            ]
        };

        var projection = SceneImagePovProjection.Resolve(
            Brief(), "Dean", Blocking(), suppression: overrides, cameraHolderPartsMode: CameraHolderPartsMode.Derived);

        // A character edit alone must not disturb a scene-level fact: one list of projection CONTENT keys, applied once.
        Assert.Equal("the camera holder's penis on Becky's vagina (buried to the root); the camera holder's wardrobe: Trousers open", projection.CameraHolderParts);
    }

    /// <summary>
    /// The pictured person's position keeps the facts the render needs and loses the clause that puts her body against
    /// the holder's, because anonymising the holder left "directly in front of the camera … facing away from him …
    /// against the camera's hips" — which a caption writer resolved by writing "the camera holder stands directly in
    /// front of her" for a frame taken from behind.
    /// </summary>
    [Fact]
    public void Resolve_ParticipantPositionKeepsSpatialFactsAndDropsTheHoldersBody()
    {
        var projection = SceneImagePovProjection.Resolve(
            Brief(), "Dean", Blocking(),
            suppression: CharacterOverride("p0", "position", BeckyPosition),
            cameraHolderPartsMode: CameraHolderPartsMode.Derived);

        var subject = Assert.Single(projection.InFrame);

        Assert.Contains("directly in front of the camera", subject.Position, StringComparison.Ordinal);
        Assert.Contains("her hips are raised at about the camera's waist height", subject.Position, StringComparison.Ordinal);

        // Gone: the contact with the holder's body, and the pronoun that could only mean him.
        Assert.DoesNotContain("the camera's hips", subject.Position, StringComparison.Ordinal);
        Assert.DoesNotContain("against", subject.Position, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("him", subject.Position, StringComparison.OrdinalIgnoreCase);
        // Anonymised, never left as the raw name.
        Assert.DoesNotContain("Dean", subject.Position, StringComparison.Ordinal);
    }

    /// <summary>
    /// The holder's own facts are what the frame is built from, so a dropped clause must never remove the posture the
    /// camera height is read from.
    /// </summary>
    [Fact]
    public void Resolve_ParticipantPositionKeepsTheSubjectsOwnPosture()
    {
        var projection = SceneImagePovProjection.Resolve(
            Brief(), "Dean", Blocking(),
            suppression: CharacterOverride("p0", "position", BeckyPosition),
            cameraHolderPartsMode: CameraHolderPartsMode.Derived);

        var subject = Assert.Single(projection.InFrame);
        Assert.Contains("torso low over the workbench", subject.Position, StringComparison.Ordinal);
    }

    /// <summary>
    /// An OMNISCIENT frame is an external view, so the relation to the camera is real content and nothing is dropped.
    /// </summary>
    [Fact]
    public void Resolve_OmniscientPositionIsNotNarrowed()
    {
        var projection = SceneImagePovProjection.Resolve(
            Brief(), SceneImagePovFramer.Omniscient, Blocking(),
            suppression: CharacterOverride("p0", "position", BeckyPosition),
            cameraHolderPartsMode: CameraHolderPartsMode.Derived);

        var subject = Assert.Single(projection.InFrame, character => character.Name == "Becky");
        Assert.Contains("against Dean's hips", subject.Position, StringComparison.Ordinal);
    }

    /// <summary>
    /// A part in the frame brings what covers it: the operator asked for "the camera holder's thighs pressed tightly
    /// against her ass, keeping what he is wearing (undone jeans)" (2026-10-25). His wardrobe is otherwise unreachable —
    /// a participant POV drops the holder's own character rows — so it travels with the visible-body note, from his own
    /// operator-editable `clothing` fact.
    /// </summary>
    [Fact]
    public void Resolve_CameraHolderNoteCarriesHisWardrobeWhenHisBodyIsInFrame()
    {
        var projection = SceneImagePovProjection.Resolve(
            BriefWithTrousers(), "Dean", Blocking(),
            suppression: ContactOverride("p1|thighs|p0|ass|pressed tightly"),
            cameraHolderPartsMode: CameraHolderPartsMode.Derived);

        Assert.Equal(
            "the camera holder's thighs on Becky's ass (pressed tightly); "
            + "the camera holder's wardrobe: Rugged casual clothing, disheveled, trousers open at the waist",
            projection.CameraHolderParts);
    }

    [Fact]
    public void Resolve_CameraHolderNoteCarriesNoWardrobeWhenHisBodyIsOutOfFrame()
    {
        // No contacts means no visible part, so there is nothing for a wardrobe fact to describe.
        var brief = BriefWithTrousers() with
        {
            SemanticInputSnapshotJson = BriefWithTrousers().SemanticInputSnapshotJson.Replace(
                "\"contacts\":[{\"from\":\"p1\",\"part\":\"penis\",\"to\":\"p0\",\"targetPart\":\"vagina\",\"manner\":\"buried to the root\"}]",
                "\"contacts\":[]",
                StringComparison.Ordinal)
        };

        var projection = SceneImagePovProjection.Resolve(
            brief, "Dean", Blocking(), cameraHolderPartsMode: CameraHolderPartsMode.Derived);

        Assert.Null(projection.CameraHolderParts);
    }

    [Fact]
    public void Resolve_CameraHolderWardrobeIsDroppedWhenTheOperatorRemovesIt()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields =
            [
                new ScenePromptFieldOverride
                {
                    ElementKey = "frozenState.contacts",
                    Value = "p1|thighs|p0|ass|pressed tightly"
                },
                new ScenePromptFieldOverride
                {
                    ElementKey = "character:faee1ec0-1cf3-459e-97d2-ad59717c41ba.clothing",
                    Removed = true
                }
            ]
        };

        var projection = SceneImagePovProjection.Resolve(
            BriefWithTrousers(), "Dean", Blocking(),
            suppression: overrides,
            cameraHolderPartsMode: CameraHolderPartsMode.Derived);

        Assert.Equal("the camera holder's thighs on Becky's ass (pressed tightly)", projection.CameraHolderParts);
    }

    private static CompiledMediaBrief BriefWithTrousers()
    {
        var brief = Brief();
        return brief with
        {
            SemanticInputSnapshotJson = brief.SemanticInputSnapshotJson.Replace(
                "\"clothing\":\"Trousers open\"",
                "\"clothing\":\"Rugged casual clothing, disheveled, trousers open at the waist\"",
                StringComparison.Ordinal)
        };
    }

    private static ScenePromptOverrides ContactOverride(string contacts) => new()
    {
        Fields =
        [
            new ScenePromptFieldOverride { ElementKey = "frozenState.contacts", Value = contacts }
        ]
    };

    private static ScenePromptOverrides CharacterOverride(string characterId, string leaf, string value) => new()
    {
        Fields =
        [
            new ScenePromptFieldOverride { ElementKey = $"character:{characterId}.{leaf}", Value = value }
        ]
    };

    private static CompiledMediaBrief Brief() => new(
        Id: "brief-operator-edits",
        MediaKind: MediaProductionKind.StillImage,
        TargetProfileId: "canonical-still",
        TargetProfileVersion: "3",
        FamilyKey: "canonical",
        CompilerKey: "deterministic",
        CompilerVersion: "1",
        ProviderRequestContractVersion: "canonical-request-v1",
        Lineage: new CompiledMediaLineage("cat1", "beat1", "plan1", 1, "momset1", 1, "m2", "enr1", 3),
        CanonicalSourceIds: ["src1"],
        SemanticInputSnapshotJson: FrozenState,
        ProviderRequestSnapshotJson: """{"contract":"canonical-request-v1"}""",
        RequiredIntentCoverageJson: """{"entries":[]}""",
        Status: MediaCompilerStatus.Complete,
        ErrorCode: null,
        ErrorMessage: null,
        CreatedUtc: DateTime.UtcNow.AddMinutes(-1),
        CompletedUtc: DateTime.UtcNow);

    private const string FrozenState = """
        {"frozenState":{
          "characters":[
            {"profileKey":"p0","characterId":"f58f959a-8050-4388-a219-99d2df3446a1","name":"Becky","involvement":"active",
             "physicalLocation":"At the workbench inside the shed","position":"Bent over the workbench","actionOrObservation":"Biting her own forearm","sightline":"down toward her own forearm","visibleCharacterNames":["Dean"],"clothing":"Nude",
             "at":{"anchor":"workbench","posture":"bent over at the waist"},"facing":{"toward":"the tin wall"}},
            {"profileKey":"p1","characterId":"faee1ec0-1cf3-459e-97d2-ad59717c41ba","name":"Dean","involvement":"active",
             "physicalLocation":"Behind Becky at the workbench","position":"Standing directly behind Becky","actionOrObservation":"Holding his position","sightline":"down toward Becky's back","visibleCharacterNames":["Becky"],"clothing":"Trousers open",
             "at":{"anchor":"shed floor behind the workbench","posture":"standing"},"facing":{"toward":"Becky's back"}}],
          "location":"Shed behind the trailers","timeOfDay":"Morning","lighting":"Bars of slat light","environment":"Shaded shed interior","mood":"Clenched","objects":["workbench"],
          "contacts":[{"from":"p1","part":"penis","to":"p0","targetPart":"vagina","manner":"buried to the root"}],
          "event":{"anchor":"workbench","line":"Becky climaxes while Dean remains pressed to her."}}}
        """;

    private static SceneBlocking Blocking() => new(
        "1",
        [
            new SceneBlockingSubject("p0", new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground), "the person in the CENTER in the foreground"),
            new SceneBlockingSubject("p1", new FramePosition(FrameHorizontal.Left, FrameDepth.Foreground), "the person on the LEFT in the foreground")
        ],
        new SceneBlockingCamera("eye level", "medium shot", null, null, CameraHeights.Eye),
        ["p0", "p1"],
        "Two-shot at the workbench");
}
