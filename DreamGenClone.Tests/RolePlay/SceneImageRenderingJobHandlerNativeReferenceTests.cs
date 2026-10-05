using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The native-reference render path (Qwen-Image-2.1): it must never degrade into a prompt-only render
/// and must never drop the references. These tests pin the fail-fast contract on the render handler
/// itself, using a real repository so the persisted failure state is asserted too.
/// </summary>
public sealed class SceneImageRenderingJobHandlerNativeReferenceTests
{
    [Fact]
    public async Task HandleAsync_NativeReferenceWithoutAnyReferences_FailsFastAndNeverCallsAPromptOnlyClient()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewImage();
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();

        var handler = fixture.CreateHandler(referenceClient);
        var job = fixture.JobFor(image);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(job, CancellationToken.None));

        Assert.Contains("at least one reference image", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, referenceClient.Calls);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);

        var persisted = await fixture.Repository.GetImageAsync(image.Id);
        Assert.NotNull(persisted);
        Assert.Equal(SceneImageStatus.Failed, persisted!.Status);
        Assert.Contains("at least one reference image", persisted.ErrorMessage!, StringComparison.Ordinal);
        Assert.Null(persisted.FileRelativePath);
    }

    [Fact]
    public async Task HandleAsync_PoseOnNativeCapableModel_TravelsAsAReferenceAndNeverUsesTheControlNetPath()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewImage();
        image.SettingsJson = """{"poseReference":{"storagePath":"poses/standing-open.png","strength":1.0}}""";
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();

        var handler = fixture.CreateHandler(
            referenceClient,
            new StubReferenceStrategies(native: true),
            new StubSceneImageStorage(SkeletonBytes));

        await handler.HandleAsync(fixture.JobFor(image), CancellationToken.None);

        // Measured 2026-09-23: a native-reference model reads an OpenPose skeleton as pose guidance, so the pose
        // travels as one more reference image instead of forcing the ControlNet graph.
        var request = Assert.Single(referenceClient.Requests);
        var reference = Assert.Single(request.References);
        Assert.Equal(SkeletonBytes, reference.Content);
        Assert.Contains("pose reference", reference.SemanticRole, StringComparison.Ordinal);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);
    }

    [Fact]
    public async Task HandleAsync_PoseOnAModelThatQualifiesNoPoseRoute_FailsFastWithTheReason()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewImage();
        image.SettingsJson = """{"poseReference":{"storagePath":"poses/standing-open.png","strength":1.0}}""";
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();

        var handler = fixture.CreateHandler(
            referenceClient,
            new StubReferenceStrategies(native: false),
            new StubSceneImageStorage(SkeletonBytes));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(fixture.JobFor(image), CancellationToken.None));

        Assert.Contains("cannot carry it", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, referenceClient.Calls);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);
    }

    [Fact]
    public async Task HandleAsync_IdentityAndPoseOnNativeCapableModel_TravelInOneCall()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewIdentityImage();
        image.SettingsJson = """{"poseReference":{"storagePath":"poses/standing-open.png","strength":0.8}}""";
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face) = ApprovedPackAndFace();

        var handler = fixture.CreateIdentityHandler(
            new StubReferenceStrategies(native: true), referenceClient, FaceBytes, pack, face,
            new StubSceneImageStorage(SkeletonBytes));

        await handler.HandleAsync(fixture.JobFor(image), CancellationToken.None);

        var request = Assert.Single(referenceClient.Requests);
        Assert.Equal(2, request.References.Count);
        Assert.Equal(FaceBytes, request.References[0].Content);
        Assert.Equal(SkeletonBytes, request.References[1].Content);
        Assert.Contains("pose reference", request.References[1].SemanticRole, StringComparison.Ordinal);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);
    }

    [Fact]
    public async Task HandleAsync_IdentityRenderOnNativeCapableModel_SendsTheApprovedFaceAndNeverTheMechanismPath()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewIdentityImage();
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face) = ApprovedPackAndFace();

        var handler = fixture.CreateIdentityHandler(
            new StubReferenceStrategies(native: true), referenceClient, FaceBytes, pack, face);

        await handler.HandleAsync(fixture.JobFor(image), CancellationToken.None);

        var request = Assert.Single(referenceClient.Requests);
        var reference = Assert.Single(request.References);
        Assert.Equal(FaceBytes, reference.Content);
        Assert.Equal("face-1.png", reference.FileName);
        Assert.Contains("Becky", reference.SemanticRole, StringComparison.Ordinal);
        Assert.Equal(SceneImageReferenceFaceView.Front, face.FaceView);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);

        var persisted = await fixture.Repository.GetImageAsync(image.Id);
        Assert.Equal(SceneImageStatus.Complete, persisted!.Status);
        Assert.Equal("session-1/" + image.Id + ".png", persisted.FileRelativePath);
    }

    [Fact]
    public async Task HandleAsync_IdentityRenderWithUnapprovedPack_FailsFastAndNeverCallsAClient()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewIdentityImage();
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face) = ApprovedPackAndFace();
        pack.Status = CharacterImageIdentityPackStatus.Draft;

        var handler = fixture.CreateIdentityHandler(
            new StubReferenceStrategies(native: true), referenceClient, FaceBytes, pack, face);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(fixture.JobFor(image), CancellationToken.None));

        Assert.Contains("not Approved", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, referenceClient.Calls);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);

        var persisted = await fixture.Repository.GetImageAsync(image.Id);
        Assert.Equal(SceneImageStatus.Failed, persisted!.Status);
    }

    [Fact]
    public async Task HandleAsync_IdentityRenderWithoutAnyIdentityCapability_FailsFastWithTheReason()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewIdentityImage();
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face) = ApprovedPackAndFace();

        var handler = fixture.CreateIdentityHandler(
            new StubReferenceStrategies(native: false), referenceClient, FaceBytes, pack, face);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(fixture.JobFor(image), CancellationToken.None));

        Assert.Contains("cannot carry it", exception.Message, StringComparison.Ordinal);
        Assert.Contains("does not declare support", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, referenceClient.Calls);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);
    }

    /// <summary>
    /// A native-reference render whose step BOUND the character's face and build from her approved pack carries both as
    /// reference images, in the step's order (the face first, because the first reference anchors the frame).
    ///
    /// A pack image is a <c>SceneImageReferenceAsset</c> addressed by its own pack id and NOT an approved scene asset,
    /// so it cannot travel through the scene-asset resolver: before this route existed the binding was filtered out
    /// there and the build was dropped silently (reported live 2026-10-02 on the Composition Composer: "2.1 should allow
    /// for picking becky face and body but it says no approved build reference exists for this character yet").
    /// </summary>
    [Fact]
    public async Task HandleAsync_BoundPackFaceAndBuild_TravelAsTheirOwnReferenceImages()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewImage();
        image.AppliedReferenceBindingsJson = """
            [
              {"elementKey":"Identity","kind":"Face","actorKey":"becky","source":"IdentityPackAsset",
               "strategy":"NativeMultiReference","identityPackId":"pack-1","referenceAssetId":"face-1","ordinal":1},
              {"elementKey":"Body","kind":"Body","actorKey":"becky","source":"IdentityPackAsset",
               "strategy":"NativeMultiReference","identityPackId":"pack-1","referenceAssetId":"body-1","ordinal":2}
            ]
            """;
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face, body) = ApprovedPackFaceAndBody();

        var handler = fixture.CreatePackBindingHandler(
            referenceClient, new StubReferenceStrategies(native: true), pack, face, body,
            new PathKeyedIdentityStorage());

        await handler.HandleAsync(fixture.JobFor(image), CancellationToken.None);

        var request = Assert.Single(referenceClient.Requests);
        Assert.Equal(2, request.References.Count);
        Assert.Equal(FaceBytes, request.References[0].Content);
        Assert.Equal("face-1.png", request.References[0].FileName);
        Assert.Contains("approved identity face for becky", request.References[0].SemanticRole, StringComparison.Ordinal);
        Assert.Contains("Profile Left", request.References[0].SemanticRole, StringComparison.Ordinal);
        Assert.Equal(BuildBytes, request.References[1].Content);
        Assert.Equal("body-1.png", request.References[1].FileName);
        Assert.Contains("approved identity build for becky", request.References[1].SemanticRole, StringComparison.Ordinal);
        Assert.Contains("Clothed", request.References[1].SemanticRole, StringComparison.Ordinal);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);

        var persisted = await fixture.Repository.GetImageAsync(image.Id);
        Assert.Equal(SceneImageStatus.Complete, persisted!.Status);
    }

    /// <summary>
    /// A pack binding on an element a pack cannot supply is refused BY NAME rather than dropped: a pack carries faces
    /// and builds only, so a wardrobe reference that claims one is a blueprint bug — and rendering without it would be
    /// the silent drop the reference rules forbid.
    /// </summary>
    [Fact]
    public async Task HandleAsync_PackBindingOnAnElementAPackCannotSupply_FailsFastByName()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewImage();
        image.AppliedReferenceBindingsJson = """
            [
              {"elementKey":"Wardrobe","kind":"Wardrobe","actorKey":"becky","source":"IdentityPackAsset",
               "strategy":"NativeMultiReference","identityPackId":"pack-1","referenceAssetId":"dress-1","ordinal":1}
            ]
            """;
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face, body) = ApprovedPackFaceAndBody();

        var handler = fixture.CreatePackBindingHandler(
            referenceClient, new StubReferenceStrategies(native: true), pack, face, body,
            new PathKeyedIdentityStorage());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(fixture.JobFor(image), CancellationToken.None));

        Assert.Contains("carries faces and builds only", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, referenceClient.Calls);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);
    }

    /// <summary>
    /// TWO or more reference images MUST be numbered in the prompt. Qwen's own prompt-enhancer system prompt makes
    /// <c>&lt;image1&gt;</c>, <c>&lt;image2&gt;</c> … mandatory for multi-image input and requires each image's role to
    /// be stated, and ComfyUI's 2.1 encoder passes the prompt through untouched — so with no tags the model has no way
    /// to tell which reference is which, and it reproduces the wrong one. Measured 2026-10-03 (CASE-25): the same two
    /// references scored outer-ring L1 1.574 against the bound shed untagged and 0.887 tagged.
    ///
    /// <para>
    /// Composed at SEND time from the images actually being sent, not carried over from prompt generation: the reported
    /// render's prompt had been generated before the references were bound, so nothing upstream of here knew about
    /// them.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandleAsync_TwoBoundReferences_AreNumberedInThePromptInSendOrder()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewImage();
        image.AppliedReferenceBindingsJson = """
            [
              {"elementKey":"Identity","kind":"Face","actorKey":"becky","source":"IdentityPackAsset",
               "strategy":"NativeMultiReference","identityPackId":"pack-1","referenceAssetId":"face-1","ordinal":1,
               "referenceLabel":"Front"},
              {"elementKey":"Body","kind":"Body","actorKey":"becky","source":"IdentityPackAsset",
               "strategy":"NativeMultiReference","identityPackId":"pack-1","referenceAssetId":"body-1","ordinal":2,
               "referenceLabel":"Front · Unclothed"}
            ]
            """;
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face, body) = ApprovedPackFaceAndBody();

        var handler = fixture.CreatePackBindingHandler(
            referenceClient, new StubReferenceStrategies(native: true), pack, face, body,
            new PathKeyedIdentityStorage());

        await handler.HandleAsync(fixture.JobFor(image), CancellationToken.None);

        var request = Assert.Single(referenceClient.Requests);
        Assert.Equal(2, request.References.Count);
        Assert.StartsWith("two people talking in a bedroom", request.PositivePrompt, StringComparison.Ordinal);
        Assert.Contains("REFERENCE IMAGES — AUTHORITATIVE", request.PositivePrompt, StringComparison.Ordinal);

        var first = request.PositivePrompt.IndexOf("<image1>", StringComparison.Ordinal);
        var second = request.PositivePrompt.IndexOf("<image2>", StringComparison.Ordinal);
        Assert.True(first >= 0 && second > first, request.PositivePrompt);

        // The tags name the images the model RECEIVES, in the order it receives them, and each line says what that
        // image is — the face first, the build second, matching request.References above.
        var firstLine = request.PositivePrompt[first..second];
        var secondLine = request.PositivePrompt[second..];
        Assert.Contains("Front", firstLine, StringComparison.Ordinal);
        Assert.Contains("FACE reference", firstLine, StringComparison.Ordinal);
        Assert.Contains("Front · Unclothed", secondLine, StringComparison.Ordinal);
        Assert.Contains("BODY reference", secondLine, StringComparison.Ordinal);
    }

    /// <summary>
    /// ONE reference is the opposite case and must stay untagged: Qwen's rule is explicit that a single-image input
    /// uses no tags, and every single-reference render the app has ever produced was working that way.
    /// </summary>
    [Fact]
    public async Task HandleAsync_OneBoundReference_LeavesThePromptUntagged()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewImage();
        image.AppliedReferenceBindingsJson = """
            [
              {"elementKey":"Identity","kind":"Face","actorKey":"becky","source":"IdentityPackAsset",
               "strategy":"NativeMultiReference","identityPackId":"pack-1","referenceAssetId":"face-1","ordinal":1,
               "referenceLabel":"Front"}
            ]
            """;
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face, body) = ApprovedPackFaceAndBody();

        var handler = fixture.CreatePackBindingHandler(
            referenceClient, new StubReferenceStrategies(native: true), pack, face, body,
            new PathKeyedIdentityStorage());

        await handler.HandleAsync(fixture.JobFor(image), CancellationToken.None);

        var request = Assert.Single(referenceClient.Requests);
        Assert.Single(request.References);
        Assert.Equal("two people talking in a bedroom", request.PositivePrompt);
        Assert.DoesNotContain("<image1>", request.PositivePrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("REFERENCE IMAGES", request.PositivePrompt, StringComparison.Ordinal);
    }

    private static readonly byte[] FaceBytes = [9, 8, 7];

    private static readonly byte[] BuildBytes = [4, 4, 4, 4, 4];

    private static readonly byte[] SkeletonBytes = [5, 5, 5, 5];

    private static readonly byte[] LocationBytes = [6, 6, 6, 6, 6, 6];

    private static (CharacterImageIdentityPack Pack, SceneImageReferenceAsset Face) ApprovedPackAndFace()
        => (new CharacterImageIdentityPack
        {
            Id = "pack-1",
            CharacterTemplateId = "character-1",
            Version = 3,
            Status = CharacterImageIdentityPackStatus.Approved,
            CanonicalFaceAssetId = "face-1"
        },
        new SceneImageReferenceAsset
        {
            Id = "face-1",
            IdentityPackId = "pack-1",
            AssetKind = SceneImageReferenceAssetKind.Face,
            IsApproved = true,
            FaceView = SceneImageReferenceFaceView.Front,
            FileRelativePath = "identity/face-1.png",
            Sha256 = "FACEHASH"
        });

    /// <summary>
    /// The BodyComplete pack the Composition host binds from: an approved face AND an approved build, which is what the
    /// dev store holds for the character this defect was reported against.
    /// </summary>
    private static (CharacterImageIdentityPack Pack, SceneImageReferenceAsset Face, SceneImageReferenceAsset Body) ApprovedPackFaceAndBody()
        => (new CharacterImageIdentityPack
        {
            Id = "pack-1",
            CharacterTemplateId = "character-1",
            Version = 3,
            Status = CharacterImageIdentityPackStatus.Approved,
            CanonicalFaceAssetId = "face-1",
            CanonicalFullBodyAssetId = "body-1"
        },
        new SceneImageReferenceAsset
        {
            Id = "face-1",
            IdentityPackId = "pack-1",
            AssetKind = SceneImageReferenceAssetKind.Face,
            IsApproved = true,
            FaceView = SceneImageReferenceFaceView.ProfileLeft,
            FileRelativePath = "identity/face-1.png",
            Sha256 = "FACEHASH"
        },
        new SceneImageReferenceAsset
        {
            Id = "body-1",
            IdentityPackId = "pack-1",
            AssetKind = SceneImageReferenceAssetKind.FullBody,
            IsApproved = true,
            BodyView = SceneImageReferenceBodyView.Front,
            BodyState = SceneImageReferenceBodyState.Clothed,
            FileRelativePath = "identity/body-1.png",
            Sha256 = "BODYHASH"
        });

    /// <summary>
    /// THE SHAPE THAT BROKE. A step that binds a pack FACE, a pack BODY, an approved LOCATION and a POSE sends four
    /// images from three different stores, and each store is served by its own block in the render: the two pack images
    /// by the identity-pack resolver, the location by the approved-asset resolver, and the skeleton last by the pose
    /// block. Reported live 2026-10-03 as a failed render — "the reference resolver returned 1 images for 2 approved
    /// bindings" — because the caller counted the asset resolver's input as "everything that is not a pack binding",
    /// which included the pose, while the resolver selects by <c>UsesReference</c>, which excludes it. Two rules for one
    /// question: the guard fired on a render that was about to be correct.
    ///
    /// <para>
    /// The pose binding is the case that makes the two rules differ, so it is asserted here as a SENT image rather than
    /// only as an absent error: it must be the fourth reference, and it must be numbered <c>&lt;image4&gt;</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandleAsync_PackFaceAndBuildWithALocationAndAPose_SendsFourNumberedReferences()
    {
        await using var fixture = new Fixture();
        var image = fixture.NewImage();
        image.SettingsJson = """{"poseReference":{"storagePath":"poses/lying012.png","strength":1.0}}""";
        image.AppliedReferenceBindingsJson = """
            [
              {"elementKey":"Identity","kind":"Face","actorKey":"becky","source":"IdentityPackAsset",
               "strategy":"NativeMultiReference","identityPackId":"pack-1","referenceAssetId":"face-1","ordinal":1,
               "referenceLabel":"Front"},
              {"elementKey":"Body","kind":"Body","actorKey":"becky","source":"IdentityPackAsset",
               "strategy":"NativeMultiReference","identityPackId":"pack-1","referenceAssetId":"body-1","ordinal":2,
               "referenceLabel":"Front · Unclothed"},
              {"elementKey":"Location","kind":"Location","source":"ApprovedSceneAsset",
               "strategy":"NativeMultiReference","sceneAssetId":"location-asset","sceneAssetImageId":"location-image",
               "sceneAssetVersion":1,"sceneAssetSha256":"LOCATIONHASH","ordinal":3,"referenceLabel":"Indoor Back"},
              {"elementKey":"Pose","kind":"Pose","source":"PoseLibrarySkeleton",
               "strategy":"NativeMultiReference","ordinal":4,
               "skeletonRelativePath":"library/openpose-nsfw/lying012.png",
               "posePresetId":"pack-openpose-nsfw-nsfw-lying-012"}
            ]
            """;
        await fixture.Repository.InsertImageAsync(image);
        var referenceClient = new RecordingReferenceClient();
        var (pack, face, body) = ApprovedPackFaceAndBody();

        var handler = fixture.CreatePackBindingHandler(
            referenceClient, new StubReferenceStrategies(native: true), pack, face, body,
            new PathKeyedIdentityStorage(),
            referenceResolver: new MediaEditReferenceResolver(
                new StubSceneAssets(ApprovedLocationImage()), new StubSceneAssetStorageService(LocationBytes),
                new StubReferenceStrategies(native: true)),
            sceneStorage: new StubSceneImageStorage(SkeletonBytes));

        await handler.HandleAsync(fixture.JobFor(image), CancellationToken.None);

        // Four images, from three stores, in send order: the measured order is faces, then approved scene assets, then
        // the pose skeleton last.
        var request = Assert.Single(referenceClient.Requests);
        Assert.Equal(4, request.References.Count);
        Assert.Equal("face-1.png", request.References[0].FileName);
        Assert.Equal("body-1.png", request.References[1].FileName);
        Assert.Equal("location-image.png", request.References[2].FileName);
        Assert.Equal(LocationBytes, request.References[2].Content);
        Assert.Equal(SkeletonBytes, request.References[3].Content);
        Assert.Contains("pose reference", request.References[3].SemanticRole, StringComparison.Ordinal);

        // ...and the prompt numbers them in that same order, so the tag the model reads names the image it receives.
        Assert.Contains("<image1> (Front)", request.PositivePrompt, StringComparison.Ordinal);
        Assert.Contains("<image2> (Front · Unclothed)", request.PositivePrompt, StringComparison.Ordinal);
        Assert.Contains("<image3> (Indoor Back)", request.PositivePrompt, StringComparison.Ordinal);
        Assert.Contains("<image4>", request.PositivePrompt, StringComparison.Ordinal);
        Assert.Contains("there are 4", request.PositivePrompt, StringComparison.Ordinal);
        Assert.Equal(0, fixture.PromptOnlyClient.Calls);
    }

    private static SceneAssetImage ApprovedLocationImage() => new()
    {
        Id = "location-image",
        AssetId = "location-asset",
        Kind = SceneAssetKind.PromptGenerated,
        Status = SceneAssetStatus.Complete,
        DisplayName = "Indoor Back",
        Prompt = "A weathered maintenance shed, indoors.",
        FileRelativePath = "assets/location-image.png",
        ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Approved,
        ProductionVersion = 1,
        Sha256 = "LOCATIONHASH",
        ByteLength = LocationBytes.Length
    };

    private sealed class StubSceneAssets(SceneAssetImage image) : ISceneAssetRepository
    {
        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
            => Task.FromResult<SceneAssetImage?>(image.Id == imageId ? image : null);

        public Task<SceneAsset?> GetAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAsset>> ListAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAsset>> ListByPackAsync(string identityPackId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAsset>> ListByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpsertImageAsync(SceneAssetImage value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetImagePromptAsync(string imageId, string prompt, string promptCompilerId, string? negativePrompt, string? associationMetadataJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetImageCandidateDecisionAsync(string imageId, SceneAssetCandidateDecision decision, string? notes, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetImageTagsAsync(string imageId, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> AddImageTagsAsync(string imageId, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> SetImageDisplayNameAsync(string imageId, string displayName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAssetImage>> SearchImagesByTagAsync(string tagQuery, int maxResults = 200, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> ApproveImageForProductionAsync(
            string imageId, string sourceProvenanceJson, SceneAssetConsentState consentState,
            SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope,
            string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> RevokeImageApprovalAsync(string imageId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpsertAsync(SceneAsset asset, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateCandidateFieldsAsync(string assetId, string? candidateBatchId, SceneAssetCandidateDecision? candidateDecision, string? candidateNotes, string? candidateSourceAssetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RenameAsync(string assetId, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> ApproveForProductionAsync(
            string assetId, string sourceProvenanceJson, SceneAssetConsentState consentState,
            SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope,
            string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CreatePromotedAsync(SceneAsset asset, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubSceneAssetStorageService(byte[] bytes) : ISceneAssetStorageService
    {
        public Task<StoredSceneAsset> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream(bytes));

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"scene-image-native-reference-{Guid.NewGuid():N}.db");
        private readonly string _producedImagesDatabasePath = Path.Combine(Path.GetTempPath(), $"produced-images-native-reference-{Guid.NewGuid():N}.db");

        public Fixture()
        {
            Repository = new SceneImageRepository(Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={_databasePath}"
            }));
            ProducedImages = new ProducedImageRepository(Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={_producedImagesDatabasePath}"
            }));
            PromptOnlyClient = new RecordingPromptOnlyClient();
        }

        public SceneImageRepository Repository { get; }

        public ProducedImageRepository ProducedImages { get; }

        public RecordingPromptOnlyClient PromptOnlyClient { get; }

        public SceneImageRecord NewImage() => new()
        {
            Id = $"image-{Guid.NewGuid():N}",
            SessionId = "session-1",
            InteractionId = "interaction-1",
            PromptRecordId = "prompt-1",
            PromptSnapshot = "two people talking in a bedroom",
            Status = SceneImageStatus.Pending,
            RenderMode = SceneImageRenderMode.NativeReference,
            RequestedModelId = QwenImage21Model.Id,
            ImageSize = "1024x1024"
        };

        /// <summary>An identity-controlled render whose model carries identity natively (no configured mechanism).</summary>
        public SceneImageRecord NewIdentityImage()
        {
            var image = NewImage();
            image.RenderMode = SceneImageRenderMode.IdentityControlled;
            image.IdentityPacksJson = """[{"packId":"pack-1","characterLabel":"Becky"}]""";
            return image;
        }

        public BackgroundJobEnvelope JobFor(SceneImageRecord image) => new()
        {
            JobType = BackgroundJobTypes.SceneImageRendering,
            PayloadJson = JsonSerializer.Serialize(new SceneImageRenderingJobPayload
            {
                SessionId = image.SessionId,
                InteractionId = image.InteractionId,
                ImageRecordId = image.Id
            })
        };

        public SceneImageRenderingJobHandler CreateHandler(
            IReferenceConditionedImageClient referenceClient,
            IReferenceStrategyResolver? strategies = null,
            ISceneImageStorageService? storage = null)
        {
            return new SceneImageRenderingJobHandler(
                Repository,
                storage ?? new StubSceneImageStorage(),
                new StubModelResolution(),
                PromptOnlyClient,
                identityClient: null!,
                identityRequestCompiler: null!,
                poseClient: null!,
                poseResolver: null!,
                new SceneImagePromptCompilerRegistry([new StubCompiler()]),
                new TestImageCompilerProfileResolver(),
                new NullDebugEventSink(),
                NullLogger<SceneImageRenderingJobHandler>.Instance,
                ProducedImages,
                referenceConditionedClient: referenceClient,
                referenceStrategyResolver: strategies);
        }

        /// <summary>
        /// The identity render fixture: the mechanism client and the mechanism request compiler both THROW, so
        /// reaching them from a native-capability model fails the test loudly instead of passing quietly.
        /// </summary>
        public SceneImageRenderingJobHandler CreateIdentityHandler(
            IReferenceStrategyResolver strategies,
            IReferenceConditionedImageClient referenceClient,
            byte[] faceBytes,
            CharacterImageIdentityPack pack,
            SceneImageReferenceAsset face,
            ISceneImageStorageService? storage = null)
        {
            return new SceneImageRenderingJobHandler(
                Repository,
                storage ?? new StubSceneImageStorage(),
                new StubModelResolution(),
                PromptOnlyClient,
                identityClient: new NeverCalledIdentityClient(),
                identityRequestCompiler: new NeverCalledIdentityRequestCompiler(),
                poseClient: null!,
                poseResolver: null!,
                new SceneImagePromptCompilerRegistry([new StubCompiler()]),
                new TestImageCompilerProfileResolver(),
                new NullDebugEventSink(),
                NullLogger<SceneImageRenderingJobHandler>.Instance,
                ProducedImages,
                referenceConditionedClient: referenceClient,
                identityStorage: new StubIdentityStorage(faceBytes),
                referenceStrategyResolver: strategies,
                identityFaceResolver: new IdentityFaceReferenceResolver(new StubIdentityRepository(pack, face)));
        }

        /// <summary>
        /// The pack-binding fixture: the step's own bindings name a pack face and build, so both resolvers are wired
        /// over the same stub repository and the render is expected to read both images out of the pack.
        /// </summary>
        public SceneImageRenderingJobHandler CreatePackBindingHandler(
            IReferenceConditionedImageClient referenceClient,
            IReferenceStrategyResolver strategies,
            CharacterImageIdentityPack pack,
            SceneImageReferenceAsset face,
            SceneImageReferenceAsset body,
            ICharacterImageAssetStorageService storage,
            MediaEditReferenceResolver? referenceResolver = null,
            ISceneImageStorageService? sceneStorage = null)
        {
            var identity = new StubIdentityRepository(pack, face, body);
            return new SceneImageRenderingJobHandler(
                Repository,
                sceneStorage ?? new StubSceneImageStorage(),
                new StubModelResolution(),
                PromptOnlyClient,
                identityClient: null!,
                identityRequestCompiler: null!,
                poseClient: null!,
                poseResolver: null!,
                new SceneImagePromptCompilerRegistry([new StubCompiler()]),
                new TestImageCompilerProfileResolver(),
                new NullDebugEventSink(),
                NullLogger<SceneImageRenderingJobHandler>.Instance,
                ProducedImages,
                referenceConditionedClient: referenceClient,
                referenceResolver: referenceResolver,
                identityStorage: storage,
                referenceStrategyResolver: strategies,
                identityFaceResolver: new IdentityFaceReferenceResolver(identity),
                identityBodyResolver: new IdentityBodyReferenceResolver(identity));
        }

        public ValueTask DisposeAsync()
        {
            foreach (var path in new[] { _databasePath, _producedImagesDatabasePath })
            {
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                {
                    try
                    {
                        if (File.Exists(path + suffix))
                            File.Delete(path + suffix);
                    }
                    catch
                    {
                    }
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private static class QwenImage21Model
    {
        public const string Id = "8b2e4d16-3a5f-4c7e-9d10-5f6a7c8b9d20";

        public static ResolvedImageModel Resolve() => new(
            ProviderBaseUrl: "http://192.168.0.11:8188",
            ImageGenerationPath: string.Empty,
            ProviderTimeoutSeconds: 300,
            ApiKeyEncrypted: null,
            ModelIdentifier: "qwen_image_2.1_int8_convrot.safetensors",
            ContentPolicy: ImageContentPolicy.AdultAllowed,
            ProviderName: "Local ComfyUI (WOOD-GAME-MAIN 5080)",
            IsSessionOverride: false,
            SceneImageModelFamily: SceneImageModelFamily.QwenImage21,
            PromptDialect: SceneImagePromptDialect.NaturalLanguage,
            ImageProtocol: ImageProtocol.ComfyUi,
            ComfyUiUrl: "http://192.168.0.11:8188",
            QwenImage21: new QwenImage21Refs(
                UnetName: "qwen_image_2.1_int8_convrot.safetensors",
                TextEncoderName: "qwen3vl_8b_int8_convrot.safetensors",
                VaeName: "qwen_image_2.1_vae_bf16.safetensors",
                ResolutionBudget: 1024,
                MaxReferences: 16,
                Steps: 25,
                Cfg: 1.0,
                SamplerName: "euler",
                Scheduler: "simple"),
            RegisteredModelId: Id);
    }

    private sealed class StubModelResolution : IModelResolutionService
    {
        public Task<ResolvedImageModel> ResolveImageModelAsync(string? sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(QwenImage21Model.Resolve());

        public Task<ResolvedImageModel> ResolveImageModelByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => Task.FromResult(QwenImage21Model.Resolve());

        public Task<ResolvedModel> ResolveAsync(
            AppFunction function,
            string? sessionModelId = null,
            double? sessionTemperature = null,
            double? sessionTopP = null,
            int? sessionMaxTokens = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedModel> ResolveImagePromptModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneImageModelChoice>> ListSceneImageModelsAsync(bool identityCapableOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingPromptOnlyClient : IImageGenerationClient
    {
        public int Calls { get; private set; }

        public Task<byte[]?> GenerateAsync(
            ResolvedImageModel model,
            string prompt,
            string? size,
            string? negativePrompt = null,
            long? seed = null,
            CancellationToken cancellationToken = default,
            SceneImageGenerationOptions? options = null)
        {
            Calls++;
            return Task.FromResult<byte[]?>([1, 2, 3]);
        }

        public Task<(bool Success, string Message)> CheckImageModelHealthAsync(
            string baseUrl,
            string modelIdentifier,
            int timeoutSeconds,
            string? apiKeyEncrypted,
            string providerName,
            ImageContentPolicy contentPolicy,
            CancellationToken cancellationToken = default,
            ImageProtocol imageProtocol = ImageProtocol.OpenAiImages)
            => Task.FromResult((true, "ok"));
    }

    private sealed class RecordingReferenceClient : IReferenceConditionedImageClient
    {
        public int Calls { get; private set; }

        public List<ReferenceConditionedImageRequest> Requests { get; } = [];

        public Task<byte[]> GenerateWithReferencesAsync(
            ResolvedImageModel model,
            ReferenceConditionedImageRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Requests.Add(request);
            return Task.FromResult(new byte[] { 1, 2, 3 });
        }
    }

    private sealed class NullDebugEventSink : IRolePlayDebugEventSink
    {
        public Task WriteAsync(RolePlayDebugEventRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>The registry only has to hand back a compiler for the resolved family/dialect; the native-reference branch never compiles a prompt through it.</summary>
    private sealed class StubCompiler : ISceneImagePromptCompiler
    {
        public SceneImageModelFamily Family => SceneImageModelFamily.QwenImage21;

        public SceneImagePromptDialect PromptDialect => SceneImagePromptDialect.NaturalLanguage;

        public ISceneImageLLMPromptBuilder PromptBuilder => throw new NotSupportedException();

        public string CanonicalNegativePrompt => string.Empty;

        public string BuildNegativePrompt(SceneImageBeat beat, string pov) => string.Empty;
    }

    /// <summary>Storage for the success path: saves report a path, and reads serve the configured bytes (the pose skeleton).</summary>
    private sealed class StubSceneImageStorage(byte[]? bytes = null) : ISceneImageStorageService
    {
        public Task<string> SaveAsync(
            string sessionId, string fileName, Stream content, CancellationToken cancellationToken = default)
            => Task.FromResult($"{sessionId}/{fileName}");

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream(bytes ?? []));

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// The identity capability contract as the render asks it: this model carries identity by its own reference
    /// slots (native) or by a configured mechanism, and nothing else.
    /// </summary>
    private sealed class StubReferenceStrategies(bool native, bool mechanism = false, bool poseControlNet = false) : IReferenceStrategyResolver
    {
        public Task<ReferenceStrategyResolution> ResolveAsync(
            string modelId, string strategy, CancellationToken cancellationToken = default)
        {
            var available = strategy switch
            {
                ReferenceStrategyResolver.IdentityNativeMultiReference => native,
                ReferenceStrategyResolver.IdentityReferenceConditioning => mechanism,
                ReferenceStrategyResolver.PoseControlNet => poseControlNet,
                _ => false
            };

            return Task.FromResult(new ReferenceStrategyResolution(
                available ? ReferenceStrategyResolutionStatus.Possible : ReferenceStrategyResolutionStatus.Unqualified,
                strategy,
                available
                    ? $"'{strategy}' is declared and qualified for model '{modelId}'."
                    : $"Model '{modelId}' does not declare support for '{strategy}' in Model Manager."));
        }
    }

    private sealed class StubIdentityRepository(
        CharacterImageIdentityPack pack,
        params SceneImageReferenceAsset[] assets) : ICharacterImageIdentityRepository
    {
        public Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken = default)
            => Task.FromResult<CharacterImageIdentityPack?>(pack.Id == packId ? pack : null);

        public Task<SceneImageReferenceAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult(assets.FirstOrDefault(asset => asset.Id == assetId));

        public Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(
            string characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CharacterImageIdentityPack>> ListApprovedPacksAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack?> GetLatestApprovedPackAsync(
            string characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> UpsertDraftAsync(
            CharacterImageIdentityPack value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> ApproveAsync(
            string packId, string descriptorSnapshotJson, string canonicalFaceAssetId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> SupersedeAsync(string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeletePackAsync(string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddAssetAsync(SceneImageReferenceAsset value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(
            string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAssetProvenanceAsync(
            string assetId, string sourceLabel, SceneImageReferenceConsentState consentState,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetAssetApprovalAsync(
            string assetId, bool isApproved, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAssetQualityAsync(
            string assetId, SceneImageReferenceQuality qualityRating, string qualityNotes,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountAssetsByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Hands back distinct bytes per stored path, so a render that carries a face AND a build can be told apart from
    /// one that read the same image twice.
    /// </summary>
    private sealed class PathKeyedIdentityStorage : ICharacterImageAssetStorageService
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream(
                relativePath.Contains("body", StringComparison.Ordinal) ? BuildBytes : FaceBytes));

        public Task<StoredCharacterImageAsset> SaveAsync(
            string characterProfileId, string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubIdentityStorage(byte[] bytes) : ICharacterImageAssetStorageService
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream(bytes));

        public Task<StoredCharacterImageAsset> SaveAsync(
            string characterProfileId, string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NeverCalledIdentityClient : IIdentityConditionedImageClient
    {
        public Task<byte[]> GenerateAsync(
            ResolvedIdentityImageModel model, IdentityControlledImageRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "The configured-mechanism client must not be reached when the model carries identity natively.");
    }

    private sealed class NeverCalledIdentityRequestCompiler : IIdentityControlledRequestCompiler
    {
        public Task<CompiledIdentityRequest> CompileAsync(
            IdentityRequestCompilationInput input, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "The configured-mechanism request compiler must not be reached when the model carries identity natively.");
    }
}
