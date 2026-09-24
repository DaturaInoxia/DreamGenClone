using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.ModelManager;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The body target's one-at-a-time acquisition (B-122 Phase 0, Section B).
///
/// The rules these tests hold: a body build is a new target kind on the SAME pipeline (same build, same step
/// records, same container, one image per request); a body image is never invented — the prompt is the template
/// store's with the card line pasted in, and every view is an edit of an accepted parent, so the pack cannot end
/// up with two different bodies in it.
/// </summary>
public sealed class CharacterIdentityBodyServiceTests
{
    /// <summary>
    /// The character TEMPLATE id these tests build against. It is a real template id shape because the brief factory
    /// reads the person's appearance from the character template — the body card describes the body, not the person.
    /// </summary>
    private const string TemplateId = "8f2a1c34-0000-4000-8000-00000000be01";
    /// <summary>
    /// B-122 E-2: the body set's model and render size live in the persisted workflow settings, so the operator sets
    /// them once instead of typing a size per view. Unset stays unset — nothing guesses a size, and the grid's
    /// generate action refuses until both are configured.
    /// </summary>
    [Fact]
    public async Task ResolveViewSettings_ReturnsThePersistedBodyPair()
    {
        var world = CreateWorld();
        try
        {
            world.Templates.Settings = new ReferenceWorkflowSettings
            {
                BodyModelId = "qwen-edit",
                BodyImageSize = "1024x1536"
            };

            var settings = await world.Body.ResolveViewSettingsAsync("becky");

            Assert.Equal("qwen-edit", settings.ModelId);
            Assert.Equal("1024x1536", settings.ImageSize);
            Assert.True(settings.IsComplete);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task ResolveViewSettings_AnUnsetPair_IsReportedAsIncomplete_NotDefaulted()
    {
        var world = CreateWorld();
        try
        {
            // Cleared explicitly: the default test world configures an identity-capable body model, so leaving it in
            // place would test the configured case rather than the unset one this test is about.
            world.Templates.Settings = new ReferenceWorkflowSettings();

            var settings = await world.Body.ResolveViewSettingsAsync("becky");

            Assert.Equal(string.Empty, settings.ModelId);
            Assert.Equal(string.Empty, settings.ImageSize);
            Assert.False(settings.IsComplete);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task SaveViewSettings_NormalisesTheSize_AndCarriesTheOtherResolvedSettings()
    {
        var world = CreateWorld();
        try
        {
            world.Templates.Settings = new ReferenceWorkflowSettings
            {
                FrontModelId = "front-model",
                QualityGateMinSharpness = 250,
                CropTargetAspect = 1.0
            };

            await world.Body.SaveViewSettingsAsync("becky", "qwen-edit", "1024X1536");

            var saved = Assert.Single(world.Templates.Saved);
            Assert.Equal("becky", saved.CharacterProfileId);
            Assert.Equal("qwen-edit", saved.BodyModelId);
            Assert.Equal("1024x1536", saved.BodyImageSize);
            // The rest of the resolved settings travel with the character row, so the override does not blank them.
            Assert.Equal("front-model", saved.FrontModelId);
            Assert.Equal(250, saved.QualityGateMinSharpness);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// Identity conditioning (B-122): it is offered only when an approved reference exists, it travels from the SAME
    /// brief resolution the prompt came from, and asking for it without a reference fails instead of rendering an
    /// unconditioned body that would look exactly like a conditioned one in the candidate list.
    /// </summary>
    [Fact]
    public async Task IdentityAvailability_ReportsWhyItIsUnavailable_WhenNoPackIsApproved()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);

            var availability = await world.Body.ResolveIdentityAvailabilityAsync(build.Id);

            Assert.False(availability.IsAvailable);
            Assert.Contains("no APPROVED identity pack", availability.Reason!, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task IdentityAvailability_NamesTheApprovedPackAndFace_WhenThereIsOne()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 7, faceAssetId: "face-1");

            var availability = await world.Body.ResolveIdentityAvailabilityAsync(build.Id);

            Assert.True(availability.IsAvailable);
            Assert.Equal("pack-1", availability.PackId);
            Assert.Equal(7, availability.PackVersion);
            Assert.Equal("face-1", availability.FaceAssetId);
            Assert.Equal(ReferenceStrategyResolver.IdentityReferenceConditioning, availability.Strategy);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// Operator request 2026-09-23: "it should allow for identity as reference images". A model can carry identity
    /// WITHOUT a configured mechanism, by taking the approved face as a reference IMAGE — Qwen-Image-2.1 declares
    /// NativeMultiReference and no IdentityMechanism at all. The old check read the mechanism field alone, so it hid
    /// the option for a model that can do the job.
    /// </summary>
    [Fact]
    public async Task IdentityAvailability_IsAvailable_WhenTheModelCarriesIdentityAsNativeReferences()
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: "qwen-native");
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 7, faceAssetId: "face-1");

            var availability = await world.Body.ResolveIdentityAvailabilityAsync(build.Id);

            Assert.True(availability.IsAvailable);
            Assert.Equal(ReferenceStrategyResolver.IdentityNativeMultiReference, availability.Strategy);
            Assert.Equal("face-1", availability.FaceAssetId);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task GenerateBase_WithIdentityOnANativeReferenceModel_CarriesTheApprovedFace()
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: "qwen-native");
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 7, faceAssetId: "face-1");

            await world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "qwen-native", "1024x1536", "Becky",
                promptOverride: null,
                pose: null,
                useIdentity: true,
                identityFaceAssetId: "face-1");

            var generation = Assert.Single(world.Assets.Generated);
            Assert.NotNull(generation.Options!.Identity);
            Assert.Equal("pack-1", generation.Options.Identity!.PackId);
            Assert.Equal("face-1", generation.Options.Identity.FaceAssetId);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// Requesting identity with nothing to condition on is refused BEFORE anything is queued: a render that silently
    /// dropped the reference would be indistinguishable from a correct one.
    /// </summary>
    [Fact]
    public async Task GenerateBase_RequestingIdentityWithoutAnApprovedPack_RefusesAndQueuesNothing()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "juggernaut", "1024x1536", "Becky", promptOverride: null, pose: null, useIdentity: true));

            Assert.Contains("Identity conditioning was requested", error.Message, StringComparison.Ordinal);
            Assert.Contains("no APPROVED identity pack", error.Message, StringComparison.Ordinal);
            Assert.Empty(world.Assets.Generated);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task GenerateBase_WithAnApprovedReference_CarriesThePackAndFaceOnTheRequest()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 3, faceAssetId: "face-9");

            await world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "juggernaut", "1024x1536", "Becky", promptOverride: null, pose: null, useIdentity: true);

            var generation = Assert.Single(world.Assets.Generated);
            Assert.NotNull(generation.Options?.Identity);
            Assert.Equal("pack-1", generation.Options!.Identity!.PackId);
            Assert.Equal("face-9", generation.Options.Identity.FaceAssetId);

            // Identity and pose are separate mechanisms and are refused together, so the request carries one or none.
            Assert.Null(generation.Options.Pose);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task GenerateBase_WithoutRequestingIdentity_LeavesTheRequestUnconditioned()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            // An approved pack EXISTS: an unconditioned render must be a decision, not a consequence of the pack
            // being missing.
            world.Identity.ApproveFace("pack-1", version: 3, faceAssetId: "face-9");

            await world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "juggernaut", "1024x1536", "Becky");

            var generation = Assert.Single(world.Assets.Generated);
            Assert.Null(generation.Options?.Identity);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>The character's name must not reach a prompt, identity-conditioned or not.</summary>
    [Fact]
    public async Task GenerateBase_WithIdentity_StillKeepsTheNameOutOfThePrompt()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 3, faceAssetId: "face-9");

            var view = await world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "juggernaut", "1024x1536", "Becky", promptOverride: null, pose: null, useIdentity: true);

            Assert.DoesNotContain("Becky", view.ResolvedPromptText!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// Operator report 2026-09-22: "the reload prompt is not doing anything". The cause was a shape line that had
    /// drifted from the picked parts, and a guard that REFUSED on it — so the prompt was never compiled, the box
    /// stayed empty, and the button looked dead. Worse, it blocked generation of every body view.
    ///
    /// The picked parts are the source (only they carry per-family renderings); the line is their record. Drift is
    /// warned about and shown in the studio, never fatal.
    /// </summary>
    [Fact]
    public async Task ResolvePrompt_WithAStaleShapeLine_StillCompilesFromThePickedParts()
    {
        var world = CreateWorld(staleShapeLine: true);
        try
        {
            var build = await StartBodyBuildAsync(world);
            var key = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);

            var prompt = await world.Body.ResolvePromptAsync(build.Id, key, "juggernaut", "Becky");

            // The parts decide: the stale line's values must not appear, and the picked ones must.
            Assert.Contains("a fuller rear with a soft belly", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("waist Soft", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("bottom hourglass", prompt, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task GenerateBase_WithAStaleShapeLine_StillGenerates()
    {
        var world = CreateWorld(staleShapeLine: true);
        try
        {
            var build = await StartBodyBuildAsync(world);

            var view = await world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "juggernaut", "1024x1536", "Becky");

            Assert.Equal(CharacterIdentityAngleStatus.Pending, view.Status);
            Assert.Single(world.Assets.Generated);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// The reference ANGLE is the operator's choice (operator decision, 2026-09-22: "it needs a spot to choose the
    /// character reference face angle, just like the studio compose"). The pack's APPROVED faces are offered, canonical
    /// first so the default is the reviewed one, and an unapproved face is not offered at all.
    /// </summary>
    [Fact]
    public async Task IdentityAvailability_OffersThePacksApprovedFaceAngles_CanonicalFirst()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 3, faceAssetId: "face-front");
            world.Identity.AddApprovedFace(
                "face-profile-left", SceneImageReferenceFaceView.ProfileLeft, SceneImageReferenceQuality.Good);
            world.Identity.AddApprovedFace(
                "face-3ql", SceneImageReferenceFaceView.ThreeQuarterLeft, SceneImageReferenceQuality.NotRated);
            world.Identity.AddUnapprovedFace("face-unapproved", SceneImageReferenceFaceView.ProfileRight);

            var availability = await world.Body.ResolveIdentityAvailabilityAsync(build.Id);

            Assert.True(availability.IsAvailable);
            Assert.Equal(3, availability.Faces.Count);
            Assert.DoesNotContain(availability.Faces, face => face.AssetId == "face-unapproved");

            var canonical = availability.Faces[0];
            Assert.True(canonical.IsCanonical);
            Assert.Equal("face-front", canonical.AssetId);

            // The label is what the operator reads in the picker: angle, plus canonical/quality when they are known.
            Assert.Contains("Front", canonical.Label, StringComparison.Ordinal);
            Assert.Contains("canonical", canonical.Label, StringComparison.Ordinal);
            Assert.Contains(
                availability.Faces,
                face => face.Label.Contains("Profile left", StringComparison.Ordinal)
                    && face.Label.Contains("Good", StringComparison.Ordinal));
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task GenerateBase_WithAChosenFaceAngle_CarriesThatAsset_NotTheCanonicalOne()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 3, faceAssetId: "face-front");
            world.Identity.AddApprovedFace(
                "face-profile-left", SceneImageReferenceFaceView.ProfileLeft, SceneImageReferenceQuality.Good);

            await world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "juggernaut", "1024x1536", "Becky",
                promptOverride: null, pose: null, useIdentity: true, identityFaceAssetId: "face-profile-left");

            var generation = Assert.Single(world.Assets.Generated);
            Assert.Equal("face-profile-left", generation.Options!.Identity!.FaceAssetId);
            Assert.Equal("pack-1", generation.Options.Identity.PackId);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// A face that is not an approved asset of this pack is refused rather than swapped for the canonical one: silently
    /// conditioning on a different image than the operator picked would produce a body that looks reviewed and is not.
    /// </summary>
    [Fact]
    public async Task GenerateBase_WithAFaceThatIsNotAnApprovedAssetOfThePack_IsRefused()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 3, faceAssetId: "face-front");
            world.Identity.AddUnapprovedFace("face-unapproved", SceneImageReferenceFaceView.ProfileRight);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "juggernaut", "1024x1536", "Becky",
                promptOverride: null, pose: null, useIdentity: true, identityFaceAssetId: "face-unapproved"));

            Assert.Contains("is not an approved face of identity pack", error.Message, StringComparison.Ordinal);
            Assert.Contains("Available:", error.Message, StringComparison.Ordinal);
            Assert.Empty(world.Assets.Generated);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// Pose and identity travel TOGETHER (operator decision, 2026-09-22). The request carries both conditionings, and
    /// the graph composes them on separate edges.
    /// </summary>
    [Fact]
    public async Task GenerateBase_WithPoseAndIdentity_CarriesBothConditionings()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 3, faceAssetId: "face-front");

            await world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "juggernaut", "1024x1536", "Becky",
                promptOverride: null,
                pose: new SceneAssetPoseConditioning(BodyReferenceStance.Kneeling, 0.8),
                useIdentity: true,
                identityFaceAssetId: "face-front");

            var generation = Assert.Single(world.Assets.Generated);
            Assert.NotNull(generation.Options!.Pose);
            Assert.Equal(BodyReferenceStance.Kneeling, generation.Options.Pose!.Stance);
            Assert.NotNull(generation.Options.Identity);
            Assert.Equal("face-front", generation.Options.Identity!.FaceAssetId);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// Operator report 2026-09-22: "BigLust and Juggernaut have identity, this should be built into the ui... if the
    /// model selected does not support it then it does not try or it is not enabled in the UI".
    ///
    /// Identity needs BOTH an approved pack AND a model that declares a qualified identity STRATEGY, and the option is
    /// offered only when a render could honour it. The check calls the SAME resolver the render path calls, so
    /// "offered" and "renderable" cannot disagree.
    /// </summary>
    [Fact]
    public async Task IdentityAvailability_IsUnavailable_WhenTheBodyModelDeclaresNoIdentityStrategy()
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: "juggernaut");
        try
        {
            var build = await StartBodyBuildAsync(world);
            // The PACK is fine — the model is the reason, which is exactly the case that used to be offered anyway.
            world.Identity.ApproveFace("pack-1", version: 3, faceAssetId: "face-front");

            var availability = await world.Body.ResolveIdentityAvailabilityAsync(build.Id);

            Assert.False(availability.IsAvailable);
            Assert.Contains("cannot carry identity", availability.Reason!, StringComparison.Ordinal);
            Assert.Contains("does not declare support for", availability.Reason!, StringComparison.Ordinal);
            Assert.Contains("Model Manager", availability.Reason!, StringComparison.Ordinal);
            Assert.Equal(string.Empty, availability.Strategy);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// ...and generating with identity on that model refuses BEFORE queueing, so it fails in the place the operator is
    /// looking rather than in a render job they have to go and read.
    /// </summary>
    [Fact]
    public async Task GenerateBase_WithIdentityOnAModelThatCannotCarryIt_RefusesAndQueuesNothing()
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: "juggernaut");
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 3, faceAssetId: "face-front");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "juggernaut", "1024x1536", "Becky",
                promptOverride: null, pose: null, useIdentity: true));

            Assert.Contains("Identity conditioning was requested", error.Message, StringComparison.Ordinal);
            Assert.Contains("cannot carry identity", error.Message, StringComparison.Ordinal);
            Assert.Empty(world.Assets.Generated);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task IdentityAvailability_IsUnavailable_WhenNoBodyModelIsSet()
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: string.Empty);
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 3, faceAssetId: "face-front");

            var availability = await world.Body.ResolveIdentityAvailabilityAsync(build.Id);

            Assert.False(availability.IsAvailable);
            Assert.Contains("no body view model is set", availability.Reason!, StringComparison.Ordinal);
            Assert.Contains("Body view settings", availability.Reason!, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task SaveViewSettings_RefusesAMissingModelOrAMalformedSize_AndWritesNothing()
    {
        var world = CreateWorld();
        try
        {
            var noModel = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Body.SaveViewSettingsAsync("becky", "  ", "1024x1536"));
            Assert.Contains("model is required", noModel.Message, StringComparison.Ordinal);

            var badSize = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Body.SaveViewSettingsAsync("becky", "qwen-edit", "big"));
            Assert.Contains("not an image size", badSize.Message, StringComparison.Ordinal);

            Assert.Empty(world.Templates.Saved);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task GenerateBase_CompilesThePromptForTheModelsFamily_AndRecordsTheRequest()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);

            var view = await world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                modelId: "juggernaut",
                imageSize: "1024x1536",
                characterName: "Becky");

            Assert.Equal(SceneImageReferenceBodyState.Clothed, view.State);
            Assert.Equal(SceneImageReferenceBodyView.Front, view.View);
            Assert.Equal(CharacterIdentityAngleStatus.Pending, view.Status);
            Assert.Equal("juggernaut", view.ResolvedModelId);

            // The prompt is COMPILED for the model's family: "juggernaut" is an SDXL checkpoint, so it is the
            // natural-language brief — not the stored prose body, which the old path pasted verbatim.
            var prompt = view.ResolvedPromptText!;
            Assert.Contains("Full-body photograph", prompt, StringComparison.Ordinal);
            // The age is a maturity BAND on both families, never a numeral — a bare number is not a description, and
            // the card's age of 50 falls in the middle-aged band (see BodyReferencePromptCompiler.AgeBands).
            Assert.Contains("a middle-aged woman", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("50-year-old", prompt, StringComparison.Ordinal);
            // The person's stated facts, and the outfit from the template rather than a prompt body.
            Assert.Contains("brown hair", prompt, StringComparison.Ordinal);
            Assert.Contains("Wearing plain everyday clothing.", prompt, StringComparison.Ordinal);
            // The body parts, rendered in the family's own dialect.
            Assert.Contains("a full bust", prompt, StringComparison.Ordinal);
            Assert.Contains("a fuller rear with a soft belly", prompt, StringComparison.Ordinal);
            // §2.3 rule 2: a name cannot be rendered, so it must not appear — and the validator is handed it as a
            // forbidden token rather than trusted not to write it.
            Assert.DoesNotContain("Becky", prompt, StringComparison.OrdinalIgnoreCase);

            var generation = Assert.Single(world.Assets.Generated);
            Assert.Equal(CharacterIdentityBodyViewKey
                .Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front)
                .BatchIdFor(build.Id), generation.CandidateBatchId);
            Assert.Equal("1024x1536", generation.ImageSize);
            Assert.Equal(view.OutputArtifactId, generation.ImageId);

            // The compiled prompt states its compiler, which is what stops the render path compiling it a second time.
            Assert.Equal(BodyReferencePromptCompiler.CompilerId, generation.Options?.PromptCompilerId);
            // SDXL's negative is empty BY DESIGN, and an empty string is not the same as "no negative authored".
            Assert.Equal(string.Empty, generation.Options?.NegativePrompt);

            // One request produced exactly one image.
            Assert.Single(world.Assets.All);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task SaveBodyCard_CreatesV1_ThenRefusesAStaleVersionFromAnotherEditor()
    {
        var world = CreateWorld(completeCard: false, saveCard: false);
        try
        {
            var card = new CharacterBodyCard
            {
                CharacterTemplateId = TemplateId,
                BodyShape = "curvy, full bust, soft waist, wide hips",
                HeightBuild = "5'8\", medium build",
                Skin = "fair smooth skin",
                BodyHair = "moderate chest hair",
                Tattoos = "small flower on left forearm",
                ScarsMarks = "none",
                PubicHair = "neatly trimmed"
            };

            var saved = await world.Body.SaveBodyCardAsync(card, expectedVersion: 0);

            Assert.Equal(1, saved.Version);
            Assert.True(saved.IsReady);
            Assert.Equal(saved.Version, (await world.Body.GetBodyCardAsync(TemplateId))!.Version);

            // A second editor holding v1 saves first; the stale writer must be refused, not silently win.
            var first = await world.Body.SaveBodyCardAsync(saved, expectedVersion: 1);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Body.SaveBodyCardAsync(saved, expectedVersion: 1));

            Assert.Equal(2, first.Version);
            Assert.Contains("2", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task SaveBodyCard_RefusesACardThatNamesNoCharacter()
    {
        var world = CreateWorld(completeCard: false, saveCard: false);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Body.SaveBodyCardAsync(new CharacterBodyCard(), expectedVersion: 0));

            Assert.Contains("must name the character", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task GenerateBase_WithAnIncompleteBodyCard_RefusesAndNamesTheMissingDecision()
    {
        var world = CreateWorld(completeCard: false);
        try
        {
            var build = await StartBodyBuildAsync(world);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "juggernaut", "1024x1536", "Becky"));

            Assert.Contains("[DECIDE]", error.Message, StringComparison.Ordinal);
            Assert.Contains("Body hair (chest, stomach, arms, legs)", error.Message, StringComparison.Ordinal);
            Assert.Empty(world.Assets.Generated);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task GenerateBase_WithNoBodyCardAtAll_NamesTheCardRatherThanGeneratingFromNothing()
    {
        var world = CreateWorld(saveCard: false);
        try
        {
            var build = await StartBodyBuildAsync(world);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "juggernaut", "1024x1536", "Becky"));

            Assert.Contains("no body card", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task TheBodyService_RefusesAFaceBuild()
    {
        var world = CreateWorld();
        try
        {
            var faceBuild = await world.Builds.CreateBuildAsync(TemplateId, null, CharacterIdentityTargetKind.Face);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.GenerateAsync(
                faceBuild.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
                "juggernaut", "1024x1536", "Becky"));

            Assert.Contains("Face", error.Message, StringComparison.Ordinal);
            Assert.Contains("Body", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// An operator request (2026-09-23): a canonical angle should be RENDERABLE, not only rotatable out of an accepted
    /// image. The request goes out as a generation conditioned on the accepted BASE body plus the committed angle
    /// skeleton, with the body prompt's text followed by that angle's camera clause — the shape all four canonical
    /// angles passed with (cases <c>body-angle-34-*</c>, <c>body-profile-*</c>).
    /// </summary>
    [Theory]
    [InlineData(SceneImageReferenceBodyView.ThreeQuarterLeft)]
    [InlineData(SceneImageReferenceBodyView.ThreeQuarterRight)]
    [InlineData(SceneImageReferenceBodyView.ProfileLeft)]
    [InlineData(SceneImageReferenceBodyView.ProfileRight)]
    public async Task GenerateAngle_RendersFromTheAcceptedBaseBodyAndTheAngleSkeleton(
        SceneImageReferenceBodyView view)
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: "qwen-native");
        try
        {
            var build = await StartBodyBuildAsync(world);
            var baseKey = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            var acceptedBase = await UploadAcceptedBaseAsync(world, build.Id, baseKey);

            var angle = CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, view);
            var result = await world.Body.GenerateAsync(
                build.Id, angle, "qwen-native", "1024x1536", "Becky");

            // A GENERATION, not an edit: this is the whole point of the feature.
            var generation = Assert.Single(world.Assets.Generated);
            Assert.Empty(world.Assets.Edits);

            Assert.NotNull(generation.Options!.BodyAngle);
            var conditioning = generation.Options.BodyAngle!;
            Assert.Equal(view, conditioning.View);
            Assert.Equal(acceptedBase.Id, conditioning.SourceImageId);

            // The prompt is the compiled body prompt followed by that angle's camera clause, resolved from the store.
            Assert.Contains("full-body photograph", generation.Prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Camera", generation.Prompt, StringComparison.Ordinal);

            // No identity reference and no stance pose: the accepted body IS the identity and the skeleton IS the pose.
            Assert.Null(generation.Options.Identity);
            Assert.Null(generation.Options.Pose);

            // The row records the source it was rendered from, so the view's provenance is readable afterwards.
            Assert.Equal(acceptedBase.Id, result.InputArtifactId);
            Assert.Equal(CharacterIdentityAngleStatus.Pending, result.Status);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// Every angle is rendered from the accepted FRONT of its state — not from the edit chain's source. A profile's
    /// edit source is its own three-quarter, but the measured angle render is "the accepted front, turned", and using
    /// one base is what keeps the four angles comparable to each other.
    /// </summary>
    [Fact]
    public async Task GenerateAngle_Profile_UsesTheAcceptedFrontEvenWhenAThreeQuarterExists()
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: "qwen-native");
        try
        {
            var build = await StartBodyBuildAsync(world);
            var baseKey = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            var acceptedBase = await UploadAcceptedBaseAsync(world, build.Id, baseKey);

            var threeQuarter = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.ThreeQuarterLeft);
            var acceptedThreeQuarter = await AcceptViewAsync(world, build.Id, threeQuarter, "three-quarter.png");

            var profile = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.ProfileLeft);
            await world.Body.GenerateAsync(build.Id, profile, "qwen-native", "1024x1536", "Becky");

            var generation = world.Assets.Generated.Single(write => write.Prompt.Contains("edge-on", StringComparison.Ordinal));
            Assert.Equal(acceptedBase.Id, generation.Options!.BodyAngle!.SourceImageId);
            Assert.NotEqual(acceptedThreeQuarter.Id, generation.Options.BodyAngle.SourceImageId);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// The BACK view (operator request, 2026-09-24) is a canonical angle like the others — accepted body plus its
    /// committed skeleton — but it shows NO face, so the approved identity face reference is refused for it rather than
    /// sent: the model would be shown a face and asked not to show it.
    /// </summary>
    [Fact]
    public async Task GenerateBack_RendersFromTheAcceptedBodyAndRefusesTheIdentityFace()
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: "qwen-native");
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 3, faceAssetId: "face-9");
            var baseKey = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            var acceptedBase = await UploadAcceptedBaseAsync(world, build.Id, baseKey);
            var back = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Back);

            // Identity is refused with the reason, before anything is queued.
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.GenerateAsync(
                build.Id, back, "qwen-native", "1024x1536", "Becky",
                promptOverride: null, pose: null, useIdentity: true, identityFaceAssetId: "face-9"));
            Assert.Contains("shows no face", refusal.Message, StringComparison.Ordinal);
            Assert.Empty(world.Assets.Generated);

            // Without identity it renders from the accepted base body plus the back skeleton, like every other angle.
            await world.Body.GenerateAsync(build.Id, back, "qwen-native", "1024x1536", "Becky");

            var generation = Assert.Single(world.Assets.Generated);
            Assert.Equal(SceneImageReferenceBodyView.Back, generation.Options!.BodyAngle!.View);
            Assert.Equal(acceptedBase.Id, generation.Options.BodyAngle.SourceImageId);
            Assert.Contains("back view", generation.Prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Null(generation.Options.Identity);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// A STANCE pose is refused on an angle row — the view's pose IS its committed angle skeleton — while IDENTITY is
    /// offered there exactly as it is on the front (measured 2026-09-23: adding the face to body+skeleton passes; it is
    /// redundant, not harmful, so the operator's switch is honoured rather than refused).
    /// </summary>
    [Fact]
    public async Task GenerateAngle_AcceptsIdentityAndRefusesAStancePose()
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: "qwen-native");
        try
        {
            var build = await StartBodyBuildAsync(world);
            world.Identity.ApproveFace("pack-1", version: 3, faceAssetId: "face-9");
            var baseKey = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            await UploadAcceptedBaseAsync(world, build.Id, baseKey);
            var angle = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.ThreeQuarterLeft);

            var poseError = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.GenerateAsync(
                build.Id, angle, "qwen-native", "1024x1536", "Becky",
                promptOverride: null, pose: new SceneAssetPoseConditioning(BodyReferenceStance.Standing, 0.7)));

            Assert.Contains("takes no stance pose", poseError.Message, StringComparison.Ordinal);

            await world.Body.GenerateAsync(
                build.Id, angle, "qwen-native", "1024x1536", "Becky",
                promptOverride: null, pose: null, useIdentity: true, identityFaceAssetId: "face-9");

            var generation = Assert.Single(world.Assets.Generated);
            Assert.Equal("face-9", generation.Options!.Identity!.FaceAssetId);
            Assert.NotNull(generation.Options.BodyAngle);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// The compiled angle prompt must fit the family ceiling. The first seeded clause was ~330 characters, so a normal
    /// 563-character body prompt became 891 and every angle render was refused with [over-length] — the operator's
    /// "Render angle does nothing" (2026-09-23).
    /// </summary>
    [Fact]
    public async Task GenerateAngle_TheCombinedPromptStaysUnderTheFamilyCeiling()
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: "qwen-native");
        try
        {
            var build = await StartBodyBuildAsync(world);
            var baseKey = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            await UploadAcceptedBaseAsync(world, build.Id, baseKey);

            foreach (var view in BodyAngleSkeletons.Available)
            {
                var key = CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, view);
                var prompt = await world.Body.ResolvePromptAsync(build.Id, key, "qwen-native", "Becky");

                Assert.True(
                    prompt.Length <= BodyPromptStructureValidator.SdxlMaxChars,
                    $"The {view} render prompt is {prompt.Length} characters, over the {BodyPromptStructureValidator.SdxlMaxChars} ceiling: {prompt}");
                Assert.Contains("Camera", prompt, StringComparison.Ordinal);
            }
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>An angle render needs the BASE accepted: without it there is no body to turn, and nothing is queued.</summary>
    [Fact]
    public async Task GenerateAngle_WithoutAnAcceptedBase_RefusesAndQueuesNothing()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Canonical(
                    SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.ThreeQuarterLeft),
                "juggernaut", "1024x1536", "Becky"));

            Assert.Contains("has not been produced yet", error.Message, StringComparison.Ordinal);
            Assert.Empty(world.Assets.Generated);
            Assert.Empty(world.Assets.Edits);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// An extended view is still NOT generatable: it is a rotation of an accepted view, and the refusal says which
    /// action produces it.
    /// </summary>
    [Fact]
    public async Task GenerateExtendedView_RefusesBecauseItIsAnEditOfAnAcceptedView()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.GenerateAsync(
                build.Id,
                CharacterIdentityBodyViewKey.Extended(SceneImageReferenceBodyState.Clothed, 45, "standing"),
                "juggernaut", "1024x1536", "Becky"));

            Assert.Contains("is an extended view", error.Message, StringComparison.Ordinal);
            Assert.Contains("Edit from accepted source", error.Message, StringComparison.Ordinal);
            Assert.Empty(world.Assets.Generated);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// Accepting a view takes the image the operator APPROVED in its candidate deck, not the last one generated.
    /// Operator report, 2026-09-23: "the clothed front is always the last one generated not the Accepted one" —
    /// approving an older attempt in the deck left the view pointing at an image nobody had approved.
    /// </summary>
    [Fact]
    public async Task Accept_TakesTheImageApprovedInTheDeck_RatherThanTheNewestAttempt()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var baseKey = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);

            // Two attempts in one row's batch, as two Generate clicks produce; the OLDER one is the approved one.
            await world.Body.GenerateAsync(build.Id, baseKey, "juggernaut", "1024x1536", "Becky");
            await world.Body.GenerateAsync(build.Id, baseKey, "juggernaut", "1024x1536", "Becky");
            var attempts = world.Assets.Generated.Select(generation => generation.ImageId).ToList();
            Assert.Equal(2, attempts.Count);

            var approved = world.Assets.All.Single(image => image.Id == attempts[0]);
            approved.CandidateDecision = SceneAssetCandidateDecision.Accepted;
            var newest = world.Assets.All.Single(image => image.Id == attempts[1]);
            Assert.NotEqual(SceneAssetCandidateDecision.Accepted, newest.CandidateDecision);

            await RecordAllPassedFindingsAsync(world, build.Id, baseKey);
            var accepted = await world.Body.AcceptAsync(build.Id, baseKey);

            Assert.Equal(approved.Id, accepted.OutputArtifactId);
            Assert.Equal(approved.Prompt, accepted.ResolvedPromptText);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// A candidate from ANOTHER view's batch cannot become this view's result: the batches exist to keep one request's
    /// images apart, so accepting across them would record a different request's image against this one.
    /// </summary>
    [Fact]
    public async Task AcceptCandidate_RefusesAnImageFromAnotherViewsBatch()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var baseKey = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            var threeQuarter = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.ThreeQuarterLeft);

            var baseImage = await UploadAcceptedBaseAsync(world, build.Id, baseKey);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.AcceptCandidateAsync(
                build.Id, threeQuarter, baseImage.Id));

            Assert.Contains("not this view's batch", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// Accepts one image of THIS view's deck and then accepts the view through the same findings gate, so the deck's
    /// accept path cannot bypass the body-invariant checks.
    /// </summary>
    [Fact]
    public async Task AcceptCandidate_RecordsTheDecision_ThenAcceptsTheViewThroughTheSameGate()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var baseKey = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            await world.Body.GenerateAsync(build.Id, baseKey, "juggernaut", "1024x1536", "Becky");
            var imageId = world.Assets.Generated.Single().ImageId;

            // Nothing reviewed yet: the view's acceptance is refused, and the refusal names the checks.
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.AcceptCandidateAsync(
                build.Id, baseKey, imageId));
            Assert.Contains("cannot be accepted", refusal.Message, StringComparison.Ordinal);
            Assert.Contains("not reviewed", refusal.Message, StringComparison.Ordinal);

            await RecordAllPassedFindingsAsync(world, build.Id, baseKey);
            var accepted = await world.Body.AcceptCandidateAsync(build.Id, baseKey, imageId);

            Assert.Equal(imageId, accepted.OutputArtifactId);
            Assert.Equal(CharacterIdentityAngleStatus.Accepted, accepted.Status);
            Assert.Equal(SceneAssetCandidateDecision.Accepted, world.Assets.All.Single(i => i.Id == imageId).CandidateDecision);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task ThreeQuarterView_IsAnEditOfTheBase_OnlyOnceTheBaseIsAccepted()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var baseKey = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            var threeQuarter = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.ThreeQuarterLeft);

            // Nothing accepted yet: the view cannot be invented from nothing.
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.EditFromAcceptedSourceAsync(
                build.Id, threeQuarter, "qwen-edit", "Becky"));
            Assert.Contains("not been produced yet", error.Message, StringComparison.Ordinal);
            Assert.Contains("Clothed Front", error.Message, StringComparison.Ordinal);

            var uploaded = await UploadAcceptedBaseAsync(world, build.Id, baseKey);

            var view = await world.Body.EditFromAcceptedSourceAsync(build.Id, threeQuarter, "qwen-edit", "Becky");

            Assert.Equal(SceneImageReferenceBodyView.ThreeQuarterLeft, view.View);
            Assert.Equal(uploaded.Id, view.InputArtifactId);
            Assert.Equal(CharacterIdentityAngleStatus.Pending, view.Status);

            var edit = Assert.Single(world.Assets.Edits);
            Assert.Equal(uploaded.Id, edit.SourceImageId);
            Assert.Equal(threeQuarter.BatchIdFor(build.Id), edit.CandidateBatchId);
            Assert.Contains("Rotate the person's whole body", edit.Prompt, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task ProfileView_IsAnEditOfTheThreeQuarterOnItsOwnSide()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var baseKey = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            await UploadAcceptedBaseAsync(world, build.Id, baseKey);

            var threeQuarterRight = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.ThreeQuarterRight);
            var profileRight = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.ProfileRight);

            // The right profile waits on the RIGHT three-quarter, not on the left one.
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.EditFromAcceptedSourceAsync(
                build.Id, profileRight, "qwen-edit", "Becky"));
            Assert.Contains("Clothed ThreeQuarterRight", error.Message, StringComparison.Ordinal);

            var accepted = await AcceptViewAsync(world, build.Id, threeQuarterRight, "three-quarter-right.png");
            var profile = await world.Body.EditFromAcceptedSourceAsync(build.Id, profileRight, "qwen-edit", "Becky");

            Assert.Equal(accepted.Id, profile.InputArtifactId);
            Assert.Contains("full profile", world.Assets.Edits.Last().Prompt, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task ExtendedView_IsRecordedByRotationAndPosition_WithNoCanonicalSlot_AndDerivesFromTheBase()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var baseKey = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.Front);
            var baseImage = await UploadAcceptedBaseAsync(world, build.Id, baseKey);

            var extended = CharacterIdentityBodyViewKey.Extended(SceneImageReferenceBodyState.Unclothed, -90, "SideLying");
            var view = await world.Body.EditFromAcceptedSourceAsync(build.Id, extended, "qwen-edit", "Becky");

            Assert.Null(view.View);
            Assert.Equal(-90, view.RotationDeg);
            Assert.Equal("SideLying", view.PositionKey);
            Assert.Equal(baseImage.Id, view.InputArtifactId);

            // The stored row keeps the axis, so the slot can never be read as a canonical one — and the base row
            // it was derived from is still there, holding its canonical front slot.
            var stored = Assert.Single(await world.Body.ListViewsAsync(build.Id), view => view.RotationDeg is not null);
            Assert.Null(stored.View);
            Assert.Equal(-90, stored.RotationDeg);
            Assert.Equal("SideLying", stored.PositionKey);
            Assert.Single(
                await world.Body.ListViewsAsync(build.Id),
                view => view.View == SceneImageReferenceBodyView.Front);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Theory]
    [InlineData(SceneImageReferenceBodyView.Front, 45, "standing", "cannot also carry")]
    [InlineData(null, null, "standing", "requires its rotation")]
    [InlineData(null, 200, "standing", "between -180 and 180")]
    [InlineData(null, 45, "", "requires a position key")]
    public void BodyViewKey_RejectsAnInvalidAxis(
        SceneImageReferenceBodyView? view, int? rotation, string? position, string expected)
    {
        var key = new CharacterIdentityBodyViewKey(
            SceneImageReferenceBodyState.Clothed, view, rotation, string.IsNullOrEmpty(position) ? null : position);

        var error = Assert.Throws<InvalidOperationException>(key.Validate);

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptingTheBase_CompletesTheAcquisitionStep_AndMovesTheBuildOn()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            Assert.Equal(CharacterIdentityBuildStep.Front, build.CurrentStep);

            await UploadAcceptedBaseAsync(
                world, build.Id,
                CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front));

            var steps = await world.Builds.ListStepsAsync(build.Id);
            var front = Assert.Single(steps, step => step.Step == CharacterIdentityBuildStep.Front);
            Assert.Equal(CharacterIdentityBuildStepStatus.Complete, front.Status);

            var updated = await world.Builds.GetBuildAsync(build.Id);
            Assert.Equal(CharacterIdentityBuildStep.Validate, updated!.CurrentStep);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task UploadRecordsTheArtifactWithItsExplicitState_AndNextRequestReusesTheRow()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var key = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.Front);

            var uploaded = await world.Body.UploadAsync(
                build.Id, key, "body-front.png", new MemoryStream([1, 2, 3]));

            Assert.Equal(SceneImageReferenceBodyState.Unclothed, uploaded.State);
            Assert.Equal(SceneImageReferenceBodyView.Front, uploaded.View);
            Assert.Equal(CharacterIdentityAngleStatus.Complete, uploaded.Status);

            // Re-preparing the same request reuses its row: one request, one row, one slot.
            var again = await world.Body.GetViewAsync(build.Id, key);
            Assert.Equal(uploaded.Id, again!.Id);
            Assert.Single(await world.Body.ListViewsAsync(build.Id));
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task UnclothedBase_IsAnEditOfTheAcceptedClothedBase_AndTheClothedBaseIsNotEditable()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var clothed = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            var unclothed = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.Front);

            // The clothed base is the root of the chain: there is nothing to derive it from.
            var rootError = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.EditFromAcceptedSourceAsync(
                build.Id, clothed, "qwen-edit", "Becky"));
            Assert.Contains("is a base", rootError.Message, StringComparison.Ordinal);

            var acceptedClothed = await UploadAcceptedBaseAsync(world, build.Id, clothed);

            var derived = await world.Body.EditFromAcceptedSourceAsync(build.Id, unclothed, "qwen-edit", "Becky");

            Assert.Equal(SceneImageReferenceBodyState.Unclothed, derived.State);
            Assert.Equal(acceptedClothed.Id, derived.InputArtifactId);
            Assert.Equal(unclothed.BatchIdFor(build.Id), world.Assets.Edits.Last().CandidateBatchId);
        }
        finally
        {
            world.Dispose();
        }
    }

    // ── Section C: the body-invariant findings gate (B122-011…014) ──────────────────────────────────────────

    [Fact]
    public async Task Accept_RefusesAViewThatWasNeverReviewed_NamingEveryUnreviewedCheck()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var key = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            await world.Body.UploadAsync(build.Id, key, "base.png", new MemoryStream([1]));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Body.AcceptAsync(build.Id, key));

            // Silence is not a pass: the gate names the view and every check nobody recorded.
            Assert.Contains("Clothed Front body view", error.Message, StringComparison.Ordinal);
            Assert.Contains("body shape and proportions (not reviewed)", error.Message, StringComparison.Ordinal);
            Assert.Contains("tattoos and marks (design and exact placement) (not reviewed)", error.Message, StringComparison.Ordinal);
            Assert.Contains("skin, body hair and pubic hair (not reviewed)", error.Message, StringComparison.Ordinal);
            Assert.Contains("anatomy review (not reviewed)", error.Message, StringComparison.Ordinal);
            Assert.Contains("override", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Accept_RefusesAViewWhoseCheckFailed_EvenWhenTheRestPassed()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var key = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            await world.Body.UploadAsync(build.Id, key, "base.png", new MemoryStream([1]));
            await world.Body.RecordFindingsAsync(
                build.Id,
                key,
                new Dictionary<CharacterIdentityBodyCheck, CharacterIdentityBodyCheckVerdict>
                {
                    [CharacterIdentityBodyCheck.BodyShapeConsistency] = CharacterIdentityBodyCheckVerdict.Pass,
                    [CharacterIdentityBodyCheck.TattoosAndMarks] = CharacterIdentityBodyCheckVerdict.Fail,
                    [CharacterIdentityBodyCheck.SkinBodyAndPubicHair] = CharacterIdentityBodyCheckVerdict.Pass,
                    [CharacterIdentityBodyCheck.AnatomyReview] = CharacterIdentityBodyCheckVerdict.Pass
                },
                reviewer: "reviewer-1",
                note: "the forearm tattoo is on the wrong arm");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Body.AcceptAsync(build.Id, key));

            Assert.Contains("tattoos and marks (design and exact placement) (failed)", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("body shape and proportions", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Findings_AreAttributedAndTimeStamped_AndBelongToOneViewAndStateOnly()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var clothedFront = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            var unclothedFront = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.Front);

            await world.Body.UploadAsync(build.Id, clothedFront, "clothed.png", new MemoryStream([1]));
            await world.Body.UploadAsync(build.Id, unclothedFront, "unclothed.png", new MemoryStream([1]));
            await RecordAllPassedFindingsAsync(world, build.Id, clothedFront);

            var reviewed = await world.Body.GetViewAsync(build.Id, clothedFront);
            Assert.True(reviewed!.Findings.AllPassed);
            Assert.Equal("reviewer-1", reviewed.Findings.ReviewedBy);
            Assert.NotNull(reviewed.Findings.ReviewedUtc);

            // The other state of the same canonical view is a different slot and was NOT reviewed with it.
            var other = await world.Body.GetViewAsync(build.Id, unclothedFront);
            Assert.False(other!.Findings.AllPassed);
            Assert.Null(other.Findings.ReviewedBy);
            Assert.Equal(
                CharacterIdentityBodyCheckVerdict.NotReviewed,
                other.Findings.VerdictFor(CharacterIdentityBodyCheck.BodyShapeConsistency));
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task RecordFindings_RequiresAReviewer()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var key = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.RecordFindingsAsync(
                build.Id,
                key,
                new Dictionary<CharacterIdentityBodyCheck, CharacterIdentityBodyCheckVerdict>
                {
                    [CharacterIdentityBodyCheck.AnatomyReview] = CharacterIdentityBodyCheckVerdict.Pass
                },
                reviewer: "  "));

            Assert.Contains("reviewer is required", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task AnExplicitOverride_LetsAFailedViewBeAccepted_AndIsPersistedWithItsAttribution()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var key = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.Front);
            await world.Body.UploadAsync(build.Id, key, "unclothed.png", new MemoryStream([1]));
            await world.Body.RecordFindingsAsync(
                build.Id,
                key,
                CharacterIdentityBodyChecks.All.ToDictionary(
                    check => check,
                    check => check == CharacterIdentityBodyCheck.AnatomyReview
                        ? CharacterIdentityBodyCheckVerdict.Fail
                        : CharacterIdentityBodyCheckVerdict.Pass),
                reviewer: "reviewer-1");

            await Assert.ThrowsAsync<InvalidOperationException>(() => world.Body.AcceptAsync(build.Id, key));

            await world.Body.RecordOverrideAsync(
                build.Id, key, "the anatomy is the editor model's output; accepted as-is", "reviewer-1");
            var accepted = await world.Body.AcceptAsync(build.Id, key);

            Assert.Equal(CharacterIdentityAngleStatus.Accepted, accepted.Status);
            Assert.True(accepted.ManualOverrideApplied);
            Assert.Equal("reviewer-1", accepted.ManualOverrideAuthor);
            Assert.Contains("editor model's output", accepted.ManualOverrideReason!, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task AnalyzeQuality_StoresTheSharedAnalyzersOwnResult_ForThisViewsImage()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);
            var key = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            await world.Body.UploadAsync(build.Id, key, "base.png", new MemoryStream([1]));

            var analyzed = await world.Body.AnalyzeQualityAsync(build.Id, key);

            // Same bytes, same numbers, same analyzer: whatever it reports is what the view stores.
            var expected = new ReferenceImageQualityAnalyzer().Analyze(
                new MemoryStream(world.Assets.ImageBytes), 1024, 1536, world.Assets.ImageBytes.Length);

            Assert.Equal(expected.Rating, analyzed.QualityRating);
            Assert.Equal(expected.Notes, analyzed.QualityNotes);
            Assert.NotEqual(SceneImageReferenceQuality.NotRated, analyzed.QualityRating);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// Pose is a MODEL capability, and the model decides whether the option is offered. The resolver here is the same
    /// one the render path calls, so an "available" answer is a statement that the render would be accepted.
    /// </summary>
    [Fact]
    public async Task ResolvePoseAvailability_AModelThatDeclaresTheStrategy_IsAvailable()
    {
        var world = CreateWorld();
        try
        {
            var build = await StartBodyBuildAsync(world);

            var availability = await world.Body.ResolvePoseAvailabilityAsync(build.Id);

            Assert.True(availability.IsAvailable);
            Assert.Null(availability.Reason);

            // The mechanism travels with the answer, so the panel can tell a ControlNet strength box from a route that
            // has no strength to set at all — and can tell whether a pose composes with the identity mechanism.
            Assert.Equal(ReferenceStrategyResolver.PoseControlNet, availability.Strategy);

            // The model's CONFIGURED strength travels with the answer, so the panel opens on the value proven for that
            // model rather than on a panel-local default. On the FLUX path this number decides whether the control
            // image's own strokes imprint on the render, so it is not a cosmetic default.
            Assert.Equal(world.Poses.DefaultStrength, availability.DefaultStrength);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// A model that carries references natively can carry a pose too — the skeleton travels as a reference image in the
    /// same call (measured on the local host 2026-09-23). Availability therefore cannot mean "has a ControlNet": asking
    /// only that would hide a pose the render can in fact perform, and the panel's own silent-drop guard would then be
    /// the thing standing between the operator and a working render.
    /// </summary>
    [Fact]
    public async Task ResolvePoseAvailability_AModelThatCarriesReferencesNatively_IsAvailableWithNoStrength()
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: "qwen-native");
        try
        {
            var build = await StartBodyBuildAsync(world);

            var availability = await world.Body.ResolvePoseAvailabilityAsync(build.Id);

            Assert.True(availability.IsAvailable);
            Assert.Null(availability.Reason);
            Assert.Equal(ReferenceStrategyResolver.IdentityNativeMultiReference, availability.Strategy);

            // There is no ControlNet on this route, so there is no configured strength to open the panel on. Inventing
            // one would be a value the render never reads and the operator cannot reason about.
            Assert.Null(availability.DefaultStrength);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// The operator's requirement: for a model that does not support it, the option is not enabled — and the reason is
    /// the resolver's own diagnostic, so the panel states what a failed render would have said rather than a vague
    /// "unavailable".
    /// </summary>
    [Fact]
    public async Task ResolvePoseAvailability_AModelWithoutTheCapability_CarriesTheResolversOwnReason()
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: "juggernaut");
        try
        {
            var build = await StartBodyBuildAsync(world);

            var availability = await world.Body.ResolvePoseAvailabilityAsync(build.Id);

            Assert.False(availability.IsAvailable);
            Assert.Contains("PoseControlNet", availability.Reason);
            Assert.Contains("juggernaut", availability.Reason);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// An unset model cannot be pose-conditioned, and the reason names the missing setting rather than blaming the
    /// model — nothing is defaulted in to fill the gap.
    /// </summary>
    [Fact]
    public async Task ResolvePoseAvailability_WithNoModelSet_ReportsTheMissingSetting()
    {
        var world = CreateWorld(completeCard: true, saveCard: true, staleShapeLine: false, bodyModelId: string.Empty);
        try
        {
            var build = await StartBodyBuildAsync(world);

            var availability = await world.Body.ResolvePoseAvailabilityAsync(build.Id);

            Assert.False(availability.IsAvailable);
            Assert.Contains("no body view model is set", availability.Reason);
        }
        finally
        {
            world.Dispose();
        }
    }

    private static async Task<SceneAssetImage> UploadAcceptedBaseAsync(
        World world, string buildId, CharacterIdentityBodyViewKey baseKey)
    {
        await world.Body.UploadAsync(
            buildId, baseKey, $"{baseKey.Describe()}.png", new MemoryStream([9, 9, 9]));
        await RecordAllPassedFindingsAsync(world, buildId, baseKey);
        var accepted = await world.Body.AcceptAsync(buildId, baseKey);
        return world.Assets.All.Single(image => image.Id == accepted.OutputArtifactId);
    }

    private static async Task<SceneAssetImage> AcceptViewAsync(
        World world, string buildId, CharacterIdentityBodyViewKey key, string fileName)
    {
        await world.Body.UploadAsync(buildId, key, fileName, new MemoryStream([7, 7, 7]));
        await RecordAllPassedFindingsAsync(world, buildId, key);
        var accepted = await world.Body.AcceptAsync(buildId, key);
        return world.Assets.All.Single(image => image.Id == accepted.OutputArtifactId);
    }

    /// <summary>Every check reviewed and passed, as a human reviewer would record them.</summary>
    private static Task<CharacterIdentityBodyView> RecordAllPassedFindingsAsync(
        World world, string buildId, CharacterIdentityBodyViewKey key)
        => world.Body.RecordFindingsAsync(
            buildId,
            key,
            CharacterIdentityBodyChecks.All.ToDictionary(
                check => check, _ => CharacterIdentityBodyCheckVerdict.Pass),
            reviewer: "reviewer-1");

    private static async Task<CharacterIdentityBuild> StartBodyBuildAsync(World world)
        => await world.Builds.CreateBuildAsync(TemplateId, null, CharacterIdentityTargetKind.Body);

    private static World CreateWorld(bool completeCard = true, bool saveCard = true, bool staleShapeLine = false)
    {
        return CreateWorld(completeCard, saveCard, staleShapeLine, bodyModelId: "juggernaut-identity");
    }

    private static World CreateWorld(
        bool completeCard, bool saveCard, bool staleShapeLine, string bodyModelId)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"body-service-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repository = new CharacterIdentityBuildRepository(options);
        repository.EnsureSchemaAsync().GetAwaiter().GetResult();
        var bodyCards = new CharacterBodyCardRepository(options);
        bodyCards.EnsureSchemaAsync().GetAwaiter().GetResult();

        if (saveCard)
        {
            // The card's shape line is DERIVED from its body parts, exactly as the studio's "Compose into body
            // shape" writes it, so the one body has one description.
            var card = new CharacterBodyCard
            {
                CharacterTemplateId = TemplateId,
                HeightBuild = "5'8\", medium build",
                Skin = "fair smooth skin",
                BodyHair = completeCard ? "moderate chest hair" : string.Empty,
                Tattoos = "small flower on left forearm",
                ScarsMarks = "none",
                PubicHair = "neatly trimmed",
                Axes = new CharacterBodyAxes
                {
                    BodyBuild = "average frame",
                    Adiposity = "average weight",
                    FatDistribution = "fuller rear with a soft belly",
                    BustSize = "Full",
                    HipSize = "Wide"
                }
            };
            card.BodyShape = staleShapeLine
                // Reproduces the operator's live card (Becky v10): the pickers were changed but the line was never
                // re-composed, so the line and the parts disagree. The PARTS are the source, so this must still work.
                ? "average frame, bottom hourglass, evenly distributed, bust Full, waist Soft, hips Wide, rear full"
                : card.Axes.Compose();

            bodyCards.SaveAsync(card, expectedVersion: 0).GetAwaiter().GetResult();
        }

        var assets = new StubSceneAssets();
        var builds = new CharacterIdentityBuildService(
            repository,
            assets,
            new CharacterIdentityStepPlanService(repository),
            NullLogger<CharacterIdentityBuildService>.Instance);

        var templates = new StubTemplates
        {
            // Identity-capable by default, so a test that is about PACKS or FACES is not accidentally also testing the
            // model gate. The gate itself is tested by passing a model with no identity mechanism.
            Settings = new ReferenceWorkflowSettings
            {
                BodyModelId = bodyModelId,
                BodyImageSize = "1024x1536"
            }
        };
        var identityRepository = new StubIdentityRepository();
        var poses = new StubPoseResolver();
        var body = new CharacterIdentityBodyService(
            repository,
            builds,
            bodyCards,
            templates,
            assets,
            new ReferenceImageQualityAnalyzer(),
            new BodyReferenceBriefFactory(
                new StubCharacterTemplates(), identityRepository, NullLogger<BodyReferenceBriefFactory>.Instance),
            new StubModelResolution(),
            poses,
            new StubReferenceStrategies(),
            NullLogger<CharacterIdentityBodyService>.Instance);

        return new World(body, builds, assets, templates, identityRepository, poses, dbPath);
    }

    private sealed record World(
        CharacterIdentityBodyService Body,
        CharacterIdentityBuildService Builds,
        StubSceneAssets Assets,
        StubTemplates Templates,
        StubIdentityRepository Identity,
        StubPoseResolver Poses,
        string DbPath) : IDisposable
    {
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = DbPath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    /// <summary>The template store, with the body keys resolving exactly as the seed writes them.</summary>
    private sealed class StubTemplates : IImageWorkflowTemplateService
    {
        private static readonly Dictionary<string, string> Bodies = new(StringComparer.Ordinal)
        {
            [CharacterBodyWorkflowKeys.ClothedAcquire] =
                "Full-body photograph of {CharacterName}, head to feet, standing straight and facing the camera: {BodyCard}. Wearing plain everyday clothing.",
            [CharacterBodyWorkflowKeys.UnclothedAcquire] =
                "Full-body photograph of {CharacterName}, head to feet, standing straight and facing the camera: {BodyCard}.",
            [CharacterBodyWorkflowKeys.AngleThreeQuarterLeft] = "Rotate the person's whole body to a three-quarter view facing the LEFT.",
            [CharacterBodyWorkflowKeys.AngleThreeQuarterRight] = "Rotate the person's whole body to a three-quarter view facing the RIGHT.",
            [CharacterBodyWorkflowKeys.AngleProfileLeft] = "Rotate the person's whole body to a full profile facing the LEFT.",
            [CharacterBodyWorkflowKeys.AngleProfileRight] = "Rotate the person's whole body to a full profile facing the RIGHT.",
            [CharacterBodyWorkflowKeys.ExtendedView] = "Turn the person's whole body to the requested rotation and body position.",
            [CharacterBodyWorkflowKeys.Normalize] = "Align only the body to this description: {BodyCard}.",
            // The angle-RENDER camera clauses, as the seed writes them: appended to the compiled body prompt when an
            // angle is rendered rather than rotated out of the accepted view. No {CharacterName} — the combined text is
            // validated as a body prompt, which forbids a name.
            [CharacterBodyWorkflowKeys.RenderThreeQuarterLeft] =
                "Camera slightly to the front-left, body turned three-quarters away, left side nearer the camera, facing left of frame.",
            [CharacterBodyWorkflowKeys.RenderThreeQuarterRight] =
                "Camera slightly to the front-right, body turned three-quarters away, right side nearer the camera, facing right of frame.",
            [CharacterBodyWorkflowKeys.RenderProfileLeft] =
                "Camera directly to the left, body edge-on in full left profile, nose pointing to the left of frame.",
            [CharacterBodyWorkflowKeys.RenderProfileRight] =
                "Camera directly to the right, body edge-on in full right profile, nose pointing to the right of frame.",
            [CharacterBodyWorkflowKeys.RenderBack] =
                "Camera directly behind the subject in a full back view: the back of the head, the back, the backside and the backs of the legs, the face not visible."
        };

        public Task<ImageWorkflowPromptTemplate> ResolveAsync(
            string key, string? characterProfileId, CancellationToken cancellationToken = default)
        {
            if (!Bodies.TryGetValue(key, out var body))
            {
                throw new InvalidOperationException($"No prompt template is configured for key '{key}'.");
            }

            return Task.FromResult(new ImageWorkflowPromptTemplate { Key = key, Body = body, SeedBody = body });
        }

        public Task<ReferenceWorkflowSettings> ResolveSettingsAsync(
            string? characterProfileId, CancellationToken cancellationToken = default)
            => Task.FromResult(Settings);

        /// <summary>
        /// The settings row this stub serves, so the body-service tests can prove what the body pair is resolved
        /// from and what a save actually writes.
        /// </summary>
        public ReferenceWorkflowSettings Settings { get; set; } = new();

        public List<ReferenceWorkflowSettings> Saved { get; } = [];

        public Task<ImageWorkflowPromptTemplate> ResetToSeedAsync(
            string key, ImageWorkflowPromptTemplateScope scope, string? characterProfileId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ImageWorkflowPromptTemplate>> ListTemplatesAsync(
            string? characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveTemplateAsync(
            ImageWorkflowPromptTemplate template, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveSettingsAsync(
            ReferenceWorkflowSettings settings, CancellationToken cancellationToken = default)
        {
            Saved.Add(settings);
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// The character's identity packs, as the brief factory and the render path see them. Left EMPTY by default, so
    /// the default world has no approved reference — the state most characters are actually in — and a test that
    /// wants identity opts in by approving one.
    /// </summary>
    private sealed class StubIdentityRepository : ICharacterImageIdentityRepository
    {
        public CharacterImageIdentityPack? ApprovedPack { get; set; }

        public SceneImageReferenceAsset? Face { get; set; }

        /// <summary>Every approved face in the pack — the angles the operator can choose between.</summary>
        public List<SceneImageReferenceAsset> Faces { get; } = [];

        /// <summary>Approves a face reference for the character, as the studio's approve action would.</summary>
        public void ApproveFace(string packId, int version, string faceAssetId)
        {
            ApprovedPack = new CharacterImageIdentityPack
            {
                Id = packId,
                CharacterTemplateId = TemplateId,
                Version = version,
                Status = CharacterImageIdentityPackStatus.Approved,
                CanonicalFaceAssetId = faceAssetId
            };
            Face = new SceneImageReferenceAsset
            {
                Id = faceAssetId,
                IdentityPackId = packId,
                AssetKind = SceneImageReferenceAssetKind.Face,
                FaceView = SceneImageReferenceFaceView.Front,
                IsApproved = true,
                FileRelativePath = $"{faceAssetId}.png"
            };
            Faces.Clear();
            Faces.Add(Face);
        }

        /// <summary>Adds another approved face at a different angle, as the angle pipeline would.</summary>
        public void AddApprovedFace(
            string assetId, SceneImageReferenceFaceView view, SceneImageReferenceQuality quality)
        {
            var packId = ApprovedPack?.Id
                ?? throw new InvalidOperationException("Approve the canonical face first: a face belongs to a pack.");
            Faces.Add(new SceneImageReferenceAsset
            {
                Id = assetId,
                IdentityPackId = packId,
                AssetKind = SceneImageReferenceAssetKind.Face,
                FaceView = view,
                IsApproved = true,
                QualityRating = quality,
                FileRelativePath = $"{assetId}.png"
            });
        }

        /// <summary>Adds an UNAPPROVED face, so the picker can be proven to exclude it.</summary>
        public void AddUnapprovedFace(string assetId, SceneImageReferenceFaceView view)
        {
            var packId = ApprovedPack?.Id
                ?? throw new InvalidOperationException("Approve the canonical face first: a face belongs to a pack.");
            Faces.Add(new SceneImageReferenceAsset
            {
                Id = assetId,
                IdentityPackId = packId,
                AssetKind = SceneImageReferenceAssetKind.Face,
                FaceView = view,
                IsApproved = false,
                FileRelativePath = $"{assetId}.png"
            });
        }

        public Task<CharacterImageIdentityPack?> GetLatestApprovedPackAsync(
            string characterProfileId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApprovedPack);

        public Task<SceneImageReferenceAsset?> GetAssetAsync(
            string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult(Faces.FirstOrDefault(face => face.Id == assetId));

        public Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApprovedPack?.Id == packId ? ApprovedPack : null);

        public Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(string characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> UpsertDraftAsync(CharacterImageIdentityPack pack, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> ApproveAsync(string packId, string descriptorSnapshotJson, string canonicalFaceAssetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> SupersedeAsync(string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeletePackAsync(string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddAssetAsync(SceneImageReferenceAsset asset, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(string packId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneImageReferenceAsset>>(Faces);

        public Task UpdateAssetProvenanceAsync(string assetId, string sourceLabel, SceneImageReferenceConsentState consentState, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetAssetApprovalAsync(string assetId, bool isApproved, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAssetQualityAsync(string assetId, SceneImageReferenceQuality quality, string qualityNotes, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountAssetsByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// The character TEMPLATE store — the person, which the body card does not own. Becky is stated as a 50-year-old
    /// woman with brown hair and fair skin, so a compiled prompt has every fact both target families need, and the
    /// outfit comes from here too rather than from a prompt body.
    /// </summary>
    private sealed class StubCharacterTemplates : ITemplateService
    {
        public static readonly Guid Id = Guid.Parse(TemplateId);

        public Task<TemplateDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<TemplateDefinition?>(id == Id
                ? new TemplateDefinition
                {
                    Id = Id,
                    TemplateType = TemplateType.Character,
                    Name = "Becky",
                    Gender = "Female",
                    PhysicalAttributes = new PhysicalAttributes
                    {
                        Age = "50",
                        HairStyle = "Bun",
                        HairColour = "Brown",
                        EyeColour = "blue",
                        SkinTone = "Fair",
                        SkinTexture = "smooth",
                        ClothingStyle = "plain everyday clothing"
                    }
                }
                : null);

        public Task<IReadOnlyList<TemplateDefinition>> GetAllAsync(
            TemplateType? templateType = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TemplateDefinition> SaveAsync(
            TemplateDefinition template, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateImagePathAsync(Guid id, string imagePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// The registered models these tests resolve against. "juggernaut" is an SDXL checkpoint, so the compiled body
    /// prompt is the natural-language brief — the family comes from the model, not from its name. "qwen-native" is a
    /// Qwen-Image-2.1 row: it declares no identity MECHANISM and carries identity by taking the approved face as a
    /// reference image, which is why the availability check cannot read the mechanism field alone.
    /// </summary>
    private sealed class StubModelResolution : IModelResolutionService
    {
        public Task<ResolvedImageModel> ResolveImageModelByIdAsync(
            string modelId, CancellationToken cancellationToken = default)
            => modelId switch
            {
                "juggernaut" => Task.FromResult(new ResolvedImageModel(
                    "http://localhost",
                    "/sdapi/v1/txt2img",
                    300,
                    null,
                    "juggernautXL_v9",
                    ImageContentPolicy.AdultAllowed,
                    "Local",
                    false,
                    SceneImageModelFamily.Sdxl,
                    SceneImagePromptDialect.SdxlNaturalLanguage,
                    ImageProtocol.ComfyUi)),
                "qwen-native" => Task.FromResult(new ResolvedImageModel(
                    "http://localhost",
                    "/prompt",
                    300,
                    null,
                    "qwen_image_2.1_int8_convrot.safetensors",
                    ImageContentPolicy.AdultAllowed,
                    "Local",
                    false,
                    SceneImageModelFamily.QwenImage21,
                    SceneImagePromptDialect.NaturalLanguage,
                    ImageProtocol.ComfyUi)),
                _ => throw new InvalidOperationException($"No image model with id '{modelId}' is registered.")
            };

        public Task<ResolvedModel> ResolveAsync(
            AppFunction function, string? sessionModelId = null, double? sessionTemperature = null,
            double? sessionTopP = null, int? sessionMaxTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedModel> ResolveImagePromptModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedImageModel> ResolveImageModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>Models that declare an identity mechanism. "juggernaut" does not; "juggernaut-identity" does.</summary>
        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelByIdAsync(
            string modelId, CancellationToken cancellationToken = default)
            => modelId == "juggernaut-identity"
                ? Task.FromResult(new ResolvedIdentityImageModel(
                    "http://localhost",
                    300,
                    "juggernautXL_ragnarok.safetensors",
                    ImageContentPolicy.AdultAllowed,
                    "Local",
                    SceneImageIdentityMechanism.IpAdapter,
                    "PLUS FACE (portraits)",
                    null,
                    0.8))
                : throw new ModelResolutionException(
                    $"Identity mechanism not configured or unknown for model '{modelId}'. Set it to IpAdapter or PuLid "
                    + "in Model Manager (/model-manager).");

        public Task<IReadOnlyList<SceneImageModelChoice>> ListSceneImageModelsAsync(
            bool identityCapableOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// The identity- and pose-STRATEGY declarations these tests resolve against, mirroring the real resolver's
    /// contract: a strategy is available only when the model declares it AND a proof qualifies it, and the reason names
    /// what to fix. Keyed by registered model id exactly as the render path asks it.
    ///
    /// The two mechanisms are NOT interchangeable: "juggernaut-identity" carries identity through a configured
    /// IP-Adapter graph and carries a pose through a qualified OpenPose ControlNet, while "qwen-native" has no
    /// mechanism at all and carries identity AND pose by sending the image as a reference. A model that declares
    /// neither is unavailable, which is the operator's original request ("if the model selected does not support it
    /// then it does not try or it is not enabled in the UI").
    /// </summary>
    private sealed class StubReferenceStrategies : IReferenceStrategyResolver
    {
        public Task<ReferenceStrategyResolution> ResolveAsync(
            string modelId, string strategy, CancellationToken cancellationToken = default)
        {
            if (modelId == "juggernaut-identity"
                && strategy is ReferenceStrategyResolver.IdentityReferenceConditioning
                    or ReferenceStrategyResolver.PoseControlNet)
            {
                return Task.FromResult(new ReferenceStrategyResolution(
                    ReferenceStrategyResolutionStatus.Possible,
                    strategy,
                    $"'{strategy}' is declared and qualified for model '{modelId}'."));
            }

            if (modelId == "qwen-native"
                && strategy is ReferenceStrategyResolver.IdentityNativeMultiReference)
            {
                return Task.FromResult(new ReferenceStrategyResolution(
                    ReferenceStrategyResolutionStatus.Possible,
                    strategy,
                    $"'{strategy}' is declared and qualified for model '{modelId}'."));
            }

            return Task.FromResult(new ReferenceStrategyResolution(
                ReferenceStrategyResolutionStatus.Unqualified,
                strategy,
                $"Model '{modelId}' does not declare support for '{strategy}' in Model Manager."));
        }
    }

    /// <summary>
    /// The pose (OpenPose ControlNet) resolver, keyed by registered model id exactly as the render path does it.
    /// <see cref="CapableModelId"/> names the ONE model that declares and qualifies the capability, so a test can turn
    /// it off and prove the panel reports the resolver's own reason instead of offering a switch that cannot be
    /// honoured. Defaults to the id the default world uses, so a test about something else is not also testing this.
    /// </summary>
    private sealed class StubPoseResolver : IPoseImageModelResolver
    {
        public string? CapableModelId { get; init; } = "juggernaut-identity";

        /// <summary>The configured conditioning strength this stub reports, so a test can prove it reaches the panel.</summary>
        public double DefaultStrength { get; init; } = 0.7;

        public Task<ResolvedPoseImageModel> ResolveAsync(
            string modelId, CancellationToken cancellationToken = default)
            => modelId == CapableModelId
                ? Task.FromResult(new ResolvedPoseImageModel(
                    "http://localhost",
                    300,
                    "juggernautXL_ragnarok.safetensors",
                    ImageContentPolicy.AdultAllowed,
                    "Local",
                    "thibaud-openpose-xl2/OpenPoseXL2.safetensors",
                    DefaultStrength))
                : throw new ModelResolutionException(
                    $"Model '{modelId}' does not declare the 'PoseControlNet' visual strategy in Model Manager. "
                    + "Declare it before requesting a pose render.");
    }

    /// <summary>
    /// The asset library as the body target uses it. Every image the service creates is recorded with the request
    /// it came from, so a test can prove the batch, the source and the prompt — and can count that one request
    /// produced exactly one image.
    /// </summary>
    private sealed class StubSceneAssets : ISceneAssetService
    {
        private int _counter;

        public List<SceneAssetImage> All { get; } = [];

        public List<GeneratedWrite> Generated { get; } = [];

        public List<EditWrite> Edits { get; } = [];

        public sealed record GeneratedWrite(
            string ImageId, string Prompt, string ImageSize, string? CandidateBatchId,
            SceneAssetImageGenerationOptions? Options);

        public sealed record EditWrite(string ImageId, string SourceImageId, string Prompt, string? CandidateBatchId);

        public Task<SceneAsset> CreateAssetAsync(
            string name, SceneAssetType type, string? characterProfileId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SceneAsset { Id = $"container-{++_counter}", Name = name, Type = type });

        public Task<SceneAssetImage> AddGeneratedImageAsync(
            string assetId, string prompt, string modelId, string imageSize,
            CancellationToken cancellationToken = default,
            IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null,
            string? candidateBatchId = null,
            SceneAssetImageGenerationOptions? options = null)
        {
            var image = Add(assetId, candidateBatchId);
            image.NegativePrompt = options?.NegativePrompt;
            image.PromptCompilerId = options?.PromptCompilerId;
            Generated.Add(new GeneratedWrite(image.Id, prompt, imageSize, candidateBatchId, options));
            return Task.FromResult(image);
        }

        public Task<SceneAssetImage> AddUploadedImageAsync(
            string assetId, string fileName, Stream content, CancellationToken cancellationToken = default,
            string? candidateBatchId = null)
            => Task.FromResult(Add(assetId, candidateBatchId));

        public Task<SceneAssetImage> AddDerivedImageAsync(
            string assetId, string sourceImageId, MediaEditOperationKind operation, string fileName, Stream content,
            CancellationToken cancellationToken = default, string? candidateBatchId = null)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> EnqueueImageEditAsync(
            string assetId, string sourceImageId, string editPrompt, string modelId,
            CancellationToken cancellationToken = default, string? candidateBatchId = null,
            IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null)
        {
            var image = Add(assetId, candidateBatchId);
            image.SourceImageId = sourceImageId;
            Edits.Add(new EditWrite(image.Id, sourceImageId, editPrompt, candidateBatchId));
            return Task.FromResult(image);
        }

        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
            => Task.FromResult(All.FirstOrDefault(image => image.Id == imageId));

        public Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<SceneAsset?>(new SceneAsset { Id = assetId });

        private SceneAssetImage Add(string assetId, string? candidateBatchId)
        {
            var image = new SceneAssetImage
            {
                Id = $"image-{++_counter}",
                AssetId = assetId,
                Kind = SceneAssetKind.Edited,
                Status = SceneAssetStatus.Complete,
                CandidateBatchId = candidateBatchId,
                Sha256 = $"sha-{_counter}"
            };
            All.Add(image);
            return image;
        }

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAssetImage>>(
                All.Where(image => string.Equals(image.AssetId, assetId, StringComparison.Ordinal)).ToList());

        public Task SetImagePipelineStepsAsync(string imageId, string? pipelineStepsJson, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<(SceneAsset Asset, SceneAssetImage Image, Stream Stream)> OpenImageForDownloadAsync(
            string imageId, CancellationToken cancellationToken = default)
        {
            var image = All.First(candidate => candidate.Id == imageId);
            image.Width = 1024;
            image.Height = 1536;
            image.ByteLength = ImageBytes.Length;
            return Task.FromResult<(SceneAsset, SceneAssetImage, Stream)>(
                (new SceneAsset { Id = image.AssetId }, image, new MemoryStream(ImageBytes)));
        }

        /// <summary>Real PNG bytes, so the shared quality analyzer has something genuine to measure.</summary>
        public byte[] ImageBytes { get; } = CreatePng();

        private static byte[] CreatePng()
        {
            using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                64, 96, new SixLabors.ImageSharp.PixelFormats.Rgba32(120, 90, 80));
            using var buffer = new MemoryStream();
            image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
            return buffer.ToArray();
        }

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAssetImage>>(
                All.Where(image => string.Equals(image.CandidateBatchId, candidateBatchId, StringComparison.Ordinal)).ToList());

        public Task SetImageCandidateDecisionAsync(string imageId, SceneAssetCandidateDecision decision, string? notes, CancellationToken cancellationToken = default)
        {
            // The write the acceptance path makes when it takes the deck's ACCEPTED image: recorded, so a test can
            // prove the decision was set rather than assume it.
            var image = All.FirstOrDefault(candidate => candidate.Id == imageId);
            if (image is not null)
            {
                image.CandidateDecision = decision;
            }

            return Task.CompletedTask;
        }

        public Task SetImageValidationResultAsync(string imageId, string? validationResultJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> ApproveImageForProductionAsync(string imageId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> CreateFromPromptAsync(string name, string prompt, SceneAssetType type, string modelId, string imageSize, string? candidateBatchId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> CreateFromUploadAsync(string name, SceneAssetType type, string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> EnqueueEditAsync(string sourceAssetId, string name, string editPrompt, string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task EnqueueProfilePackAsync(SceneAssetProfilePackJobPayload payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAsset>> ListAssetsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAsset>> ListAssetsByPackAsync(string identityPackId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> ApproveForProductionAsync(string assetId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(SceneAsset Asset, Stream Stream)> OpenForDownloadAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
