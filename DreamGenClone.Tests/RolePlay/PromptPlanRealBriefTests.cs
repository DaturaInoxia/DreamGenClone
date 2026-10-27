using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The plan built from a REAL compiled brief — moment <c>m2</c> revision 3 of the Becky/Dean shed scene, copied verbatim
/// out of the dev database — at all three POVs the composer offers.
/// </summary>
/// <remarks>
/// The hand-written fixture in <see cref="PromptPlanBuilderTests"/> proves the builder's rules; this one proves the rules
/// hold on a record the enrichment pipeline actually produced, including the parts a synthetic fixture would smooth over:
/// an empty <c>visibleCharacterNames</c> (so one POV shows nobody), a self-contact beside a cast-to-cast contact, four
/// recorded props, and a blocking frame whose camera is not the participant's. P1 ships in shadow mode, so this is the
/// record that makes the shadow output reviewable before any renderer reads it.
/// </remarks>
public sealed class PromptPlanRealBriefTests
{
    [Fact]
    public void Omniscient_BuildsBothCastMembersAndBothContacts()
    {
        var plan = Build(SceneImagePovFramer.Omniscient, blocking: Blocking);

        Assert.Equal(["Becky", "Dean"], plan.Subjects.Select(subject => subject.Name));
        Assert.Equal(2, plan.Contacts.Count);
        Assert.Contains(plan.Contacts, contact => contact.IsSelfContact && contact.Part == "teeth");
        Assert.Contains(plan.Contacts, contact =>
            !contact.IsSelfContact && contact.FromName == "Dean" && contact.ToName == "Becky" && contact.TargetPart == "vagina");
        // An omniscient frame has no camera holder, so no holder body can be in it.
        Assert.Null(plan.HolderBody);
        Assert.False(plan.Frame.FirstPerson);
    }

    /// <summary>
    /// Dean is the camera. His own contact with Becky is HIS body, never a pictured person touching her — the defect that
    /// made one render attribute his hands to her.
    /// </summary>
    [Fact]
    public void DeanPov_TreatsHisContactAsHisOwnBodyRatherThanAsACastContact()
    {
        var plan = Build("Dean", blocking: null);

        var subject = Assert.Single(plan.Subjects);
        Assert.Equal("Becky", subject.Name);
        Assert.DoesNotContain(plan.Contacts, contact => contact.FromProfileKey == "p1" || contact.ToProfileKey == "p1");
        Assert.NotNull(plan.HolderBody);
        Assert.Equal(["penis"], plan.HolderBody!.Parts);
        Assert.Equal("Rugged casual clothing, disheveled, trousers open at the waist", plan.HolderBody.Wardrobe);
        Assert.True(plan.Frame.FirstPerson);
    }

    /// <summary>
    /// Becky's record lists nobody as visible, so her own POV shows nobody. The plan says so instead of inventing a
    /// subject — and her visible body is read from her OWN contacts, which is where a first-person frame gets its parts.
    /// </summary>
    [Fact]
    public void BeckyPov_WithNobodyVisible_CarriesNoSubjectAndStillStatesHerOwnBody()
    {
        var plan = Build("Becky", blocking: null);

        Assert.Empty(plan.Subjects);
        Assert.Empty(plan.Contacts);
        // False because the ACTION is recorded; a plan with neither people nor action is the empty one.
        Assert.False(plan.IsEmpty);
        Assert.NotNull(plan.HolderBody);
        Assert.Equal(["teeth", "vagina"], plan.HolderBody!.Parts);
    }

    /// <summary>Every POV carries the same POV-independent world facts, because the plan is built from the same record.</summary>
    [Fact]
    public void EveryPov_CarriesTheSameWorldFacts()
    {
        foreach (var pov in new[] { SceneImagePovFramer.Omniscient, "Dean", "Becky" })
        {
            var plan = Build(pov, pov == SceneImagePovFramer.Omniscient ? Blocking : null);

            Assert.Equal("Shed behind the trailers, at the gritty wooden workbench.", plan.Setting);
            Assert.Equal("Morning", plan.TimeOfDay);
            Assert.Equal("Clenched, breathless, secretive.", plan.Mood);
            Assert.Equal(4, plan.Objects.Count);
            Assert.StartsWith("Bars of slat light", plan.Lighting, StringComparison.Ordinal);
            Assert.StartsWith("Shaded shed interior", plan.Atmosphere, StringComparison.Ordinal);
            Assert.StartsWith("Becky climaxes", plan.ActionLine, StringComparison.Ordinal);
            Assert.Equal("workbench", plan.ActionAnchor);
            Assert.Equal("e4, ~49s into beat", plan.TemporalAnchor);
            // The frame states an extent only when there is someone it applies to; with no subject in frame (Becky's
            // POV) there is no body whose inclusion could be stated.
            Assert.Equal(plan.Subjects.Count == 0 ? null : FrameInclusionExtent.WholeBody, plan.Frame.Inclusion);
        }
    }

    /// <summary>The wardrobe the operator complained was lost is a fact on the subject, not a hope in the prose.</summary>
    [Fact]
    public void SubjectWardrobe_IsCarriedAsAnAffirmativeFact()
    {
        var plan = Build("Dean", blocking: null);

        Assert.Equal("Nude, bare all over", Assert.Single(plan.Subjects).Wardrobe);
        Assert.Contains("wardrobe:Becky", plan.MustAppear);
    }

    private static PromptPlan Build(string pov, SceneBlocking? blocking)
        => PromptPlanBuilder.Build(
            PromptPlanOverrides.Apply(Snapshot),
            SceneImagePovProjection.Resolve(
                Brief,
                pov,
                blocking,
                cameraHolderPartsMode: CameraHolderPartsMode.Derived),
            blocking);

    /// <summary>The operator's edits are applied exactly as the render path applies them.</summary>
    private static class PromptPlanOverrides
    {
        public static string Apply(string snapshotJson)
            => ScenePromptOverridesApplier.ApplyOperatorProjectionEdits(snapshotJson, null);
    }

    private static SceneBlocking? Blocking => SceneBlockingJson.TryRead(Snapshot);

    private static CompiledMediaBrief Brief => new(
        Id: "brief-real-m2",
        MediaKind: MediaProductionKind.StillImage,
        TargetProfileId: "canonical-still",
        TargetProfileVersion: "3",
        FamilyKey: "canonical",
        CompilerKey: "deterministic",
        CompilerVersion: "1",
        ProviderRequestContractVersion: "canonical-request-v1",
        Lineage: new CompiledMediaLineage(
            "939a11ce-cd38-417f-965c-72373037b796", "b3", "9badc1f7-3e68-4a82-bd8d-244924968cd2", 2,
            "023a43a3-14a4-47d9-870c-9228a084447b", 1, "m2", "e1d4b9d7-1235-4b47-a20f-1c33ae739401", 3),
        CanonicalSourceIds: ["src1"],
        SemanticInputSnapshotJson: Snapshot,
        ProviderRequestSnapshotJson: """{"contract":"canonical-request-v1"}""",
        RequiredIntentCoverageJson: """{"entries":[]}""",
        Status: MediaCompilerStatus.Complete,
        ErrorCode: null,
        ErrorMessage: null,
        CreatedUtc: DateTime.UtcNow.AddMinutes(-1),
        CompletedUtc: DateTime.UtcNow);

    /// <summary>Verbatim from <c>CompiledMediaBriefs</c> (moment m2, enrichment revision 3, 2026-10-25).</summary>
    private const string Snapshot = """{"lineage":{"catalogueId":"939a11ce-cd38-417f-965c-72373037b796","beatId":"b3","beatProductionPlanId":"9badc1f7-3e68-4a82-bd8d-244924968cd2","beatProductionPlanVersion":2,"momentSetId":"023a43a3-14a4-47d9-870c-9228a084447b","momentSetVersion":1,"momentId":"m2","momentEnrichmentId":"e1d4b9d7-1235-4b47-a20f-1c33ae739401","momentEnrichmentRevision":3},"moment":{"momentId":"m2","temporalAnchor":"e4, ~49s into beat","participantSummary":[{"profileKey":"p0","involvement":"active"},{"profileKey":"p1","involvement":"active"}],"compositionRationale":"Captures the beat\u0027s most distinctive still-image detail, Becky\u0027s silent climax and forearm bite, with both participants active.","productionRoles":["StillCandidate"]},"frozenState":{"characters":[{"profileKey":"p0","characterId":"f58f959a-8050-4388-a219-99d2df3446a1","name":"Becky","involvement":"active","physicalLocation":"At the workbench inside the shed behind the trailers.","position":"Becky is bent over at the waist with her torso low over the workbench, directly in front of Dean and facing away from him toward the tin wall; her hips are raised at about Dean\u0027s waist height, her head and forearm are lower over the bench, and her body is within inches of Dean\u0027s hips.","actionOrObservation":"Bites down on her own forearm, her body clenched, holding quiet through climax.","sightline":"down toward her own forearm at her mouth","visibleCharacterNames":[],"clothing":"Nude, bare all over","viewpointPitch":null,"viewpointSubjectDistance":null,"at":{"anchor":"workbench","posture":"bent over at the waist, torso low over the workbench boards, hips raised, feet on the shed floor"},"facing":{"toward":"the tin wall beyond the workbench"}},{"profileKey":"p1","characterId":"faee1ec0-1cf3-459e-97d2-ad59717c41ba","name":"Dean","involvement":"active","physicalLocation":"Behind Becky at the workbench inside the shed.","position":"Dean is standing directly behind Becky with his hips pressed to her rear and his torso close over her back, facing forward toward her back and the workbench; he is taller than her bent form, with his waist at her raised hips and his head above her shoulders, within inches of her.","actionOrObservation":"Remains pressed to the root behind Becky, holding his position while she clenches.","sightline":"down toward Becky\u0027s back","visibleCharacterNames":["Becky"],"clothing":"Rugged casual clothing, disheveled, trousers open at the waist","viewpointPitch":null,"viewpointSubjectDistance":null,"at":{"anchor":"shed floor behind the workbench","posture":"standing with feet planted on the shed floor, hips pressed to Becky\u0027s rear, torso close over her back"},"facing":{"toward":"Becky\u0027s back"}}],"location":"Shed behind the trailers, at the gritty wooden workbench.","timeOfDay":"Morning","lighting":"Bars of slat light lying across the workbench and shed boards; shaded interior.","environment":"Shaded shed interior with grit on the boards, pine sap and rust in the air, and the tin wall close behind the workbench.","mood":"Clenched, breathless, secretive.","objects":["gritty wooden workbench","tin shed wall","wooden shed boards","slat light bars"],"contacts":[{"from":"p0","part":"teeth","to":"p0","targetPart":"forearm","manner":"sunk into"},{"from":"p1","part":"penis","to":"p0","targetPart":"vagina","manner":"buried to the root"}],"event":{"anchor":"workbench","line":"Becky climaxes with her teeth sunk into her own forearm to stay quiet while Dean remains pressed to the root behind her."}},"typedReferences":[{"referenceKey":"identity-p0","role":"CharacterIdentity","mediaKind":"Identity","sourceRecordId":null,"assetId":null,"subjectKey":"p0","window":null,"required":true},{"referenceKey":"wardrobe-p0","role":"WardrobeContinuity","mediaKind":"Wardrobe","sourceRecordId":null,"assetId":null,"subjectKey":"p0","window":null,"required":true},{"referenceKey":"identity-p1","role":"CharacterIdentity","mediaKind":"Identity","sourceRecordId":null,"assetId":null,"subjectKey":"p1","window":null,"required":true},{"referenceKey":"wardrobe-p1","role":"WardrobeContinuity","mediaKind":"Wardrobe","sourceRecordId":null,"assetId":null,"subjectKey":"p1","window":null,"required":true},{"referenceKey":"location","role":"LocationContinuity","mediaKind":"Location","sourceRecordId":null,"assetId":null,"subjectKey":null,"window":null,"required":true}],"blocking":{"profileVersion":"1","subjects":[{"subjectKey":"p0","position":{"horizontal":"Left","depth":"Foreground"},"roleText":"the person on the LEFT in the foreground, nearest the camera"},{"subjectKey":"p1","position":{"horizontal":"Right","depth":"Foreground"},"roleText":"the person on the RIGHT in the foreground, nearest the camera"}],"camera":{"angle":"eye level","shotSize":"medium shot","side":null,"omniscientShotSize":"wide establishing shot","height":"eye"},"layering":["p0","p1"],"rationale":"Captures the beat\u0027s most distinctive still-image detail, Becky\u0027s silent climax and forearm bite, with both participants active."}}""";
}
