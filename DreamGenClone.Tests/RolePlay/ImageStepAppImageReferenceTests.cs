using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Any image the app holds can stand as a composition's Location reference — a rendered image by its own id in the
/// scene-image store, an approved asset image through the channel that already served it.
///
/// <para>
/// These tests pin the two things that make that safe: the binding names its own store's ids (so neither channel is
/// written into the other's fields), and the render re-reads the image and compares the checksum the binding recorded
/// (so a reference cannot quietly condition on different bytes).
/// </para>
/// </summary>
public sealed class ImageStepAppImageReferenceTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CompositionBlueprint_LocationSlotAcceptsTheAppsOwnImages()
    {
        var blueprint = ImageStepBlueprintFactory.ForProductionStudio([new ImageStepActor("becky", "Becky")]);
        var location = Assert.Single(blueprint.Slots, slot => slot.SlotKind == ImageStepSlotKind.Location);

        Assert.Contains(ImageStepReferenceSourceKind.AppImage, location.AllowedSources);
        // Several views of one place are several references, which is why this slot is the multi one.
        Assert.True(location.AllowsMultiple);
    }

    [Fact]
    public void Planning_AnAppImageBinding_NamesItsOwnStoreAndNoAsset()
    {
        var blueprint = ImageStepBlueprintFactory.ForProductionStudio([new ImageStepActor("becky", "Becky")]);
        var planned = ReferenceSlotPlanner.Plan(
            blueprint,
            [
                new ImageStepSlotAssignment(ImageStepSlotKind.Location, null, new ImageStepSlotSource(
                    SourceKind: ImageStepReferenceSourceKind.AppImage,
                    Strategy: ReferenceStrategyResolver.IdentityNativeMultiReference,
                    ReferenceImageId: "rendered-1",
                    ReferenceImageSha256: "SHA-rendered-1",
                    ReferenceLabel: "the trailer at dusk"))
            ],
            maxReferences: 6);

        var binding = Assert.Single(planned);
        Assert.Equal(nameof(ImageStepReferenceSourceKind.AppImage), binding.Source);
        Assert.Equal("rendered-1", binding.ReferenceImageId);
        Assert.Equal("SHA-rendered-1", binding.ReferenceImageSha256);
        // Deliberately NOT filled: an approved-asset id and version are what the approved-asset resolver validates
        // against an approval a rendered image never had.
        Assert.Null(binding.SceneAssetId);
        Assert.Null(binding.SceneAssetImageId);
        Assert.Null(binding.SceneAssetVersion);

        Assert.Equal(ReferenceChannel.AppImage, ReferenceBindingShape.ChannelOf(binding));
        Assert.True(binding.SuppliesImage);
        // The reference resolver is the one that serves it, so the render's own selection must claim it.
        Assert.True(ReferenceBindingShape.IsAssetBacked(binding));
    }

    /// <summary>
    /// Every re-plan converts the current bindings back to sources and plans again, so this round-trip is what keeps a
    /// bound reference alive across an unrelated edit. It is the exact defect reported on 2026-10-18: the app-image
    /// channel was not copied back, so the FIRST re-plan produced a Location binding with no image and validation
    /// refused the render ("declares strategy 'NativeMultiReference' but names no reference image").
    /// </summary>
    [Fact]
    public void FromBinding_RoundTripsEveryChannelSoARePlanCannotDropAnImage()
    {
        // The pack-identity composition is the surface that offers every channel: pack images for face and build,
    // approved assets or app images for the place, pose skeletons for the stance.
    var blueprint = ImageStepBlueprintFactory.ForPackIdentityComposition([new ImageStepActor("becky", "Becky")]);

        var channels = new (string Name, ReferenceApplicationSelection Binding)[]
        {
            ("app image", new ReferenceApplicationSelection
            {
                ElementKey = "Location", Kind = nameof(ImageStepSlotKind.Location),
                SemanticRole = "location continuity", Strategy = ReferenceStrategyResolver.IdentityNativeMultiReference,
                Source = nameof(ImageStepReferenceSourceKind.AppImage),
                ReferenceImageId = "rendered-1", ReferenceImageSha256 = "SHA-rendered-1"
            }),
            ("approved asset", new ReferenceApplicationSelection
            {
                ElementKey = "Location", Kind = nameof(ImageStepSlotKind.Location),
                SemanticRole = "location continuity", Strategy = ReferenceStrategyResolver.IdentityNativeMultiReference,
                Source = nameof(ImageStepReferenceSourceKind.ApprovedSceneAsset),
                SceneAssetId = "asset-1", SceneAssetImageId = "image-1", SceneAssetVersion = 3,
                SceneAssetSha256 = "SHA-asset-1"
            }),
            ("identity pack", new ReferenceApplicationSelection
            {
                ElementKey = "Body", Kind = nameof(ImageStepSlotKind.Body), ActorKey = "becky",
                SemanticRole = "character body", Strategy = ReferenceStrategyResolver.IdentityNativeMultiReference,
                Source = nameof(ImageStepReferenceSourceKind.IdentityPackAsset),
                IdentityPackId = "pack-1", ReferenceAssetId = "pack-asset-1"
            }),
            ("pose", new ReferenceApplicationSelection
            {
                ElementKey = "Pose", Kind = nameof(ImageStepSlotKind.Pose),
                SemanticRole = "stance", Strategy = ReferenceStrategyResolver.IdentityNativeMultiReference,
                Source = nameof(ImageStepReferenceSourceKind.PoseLibrarySkeleton),
                PosePresetId = "preset-1", SkeletonRelativePath = "poses/preset-1.png"
            })
        };

        foreach (var (name, binding) in channels)
        {
            // The re-plan a control change performs.
            var replanned = ReferenceSlotPlanner.Plan(
                blueprint,
                [new ImageStepSlotAssignment(
                    Enum.Parse<ImageStepSlotKind>(binding.Kind!), binding.ActorKey,
                    ImageStepSlotSource.FromBinding(binding))],
                maxReferences: 10);

            var roundTripped = Assert.Single(replanned);

            Assert.True(roundTripped.SuppliesImage, $"The {name} reference lost its image on a re-plan.");
            Assert.Equal(binding.ReferenceImageId, roundTripped.ReferenceImageId);
            Assert.Equal(binding.ReferenceImageSha256, roundTripped.ReferenceImageSha256);
            Assert.Equal(binding.SceneAssetId, roundTripped.SceneAssetId);
            Assert.Equal(binding.SceneAssetImageId, roundTripped.SceneAssetImageId);
            Assert.Equal(binding.SceneAssetVersion, roundTripped.SceneAssetVersion);
            Assert.Equal(binding.SceneAssetSha256, roundTripped.SceneAssetSha256);
            Assert.Equal(binding.IdentityPackId, roundTripped.IdentityPackId);
            Assert.Equal(binding.ReferenceAssetId, roundTripped.ReferenceAssetId);
            Assert.Equal(binding.PosePresetId, roundTripped.PosePresetId);
            Assert.Equal(binding.SkeletonRelativePath, roundTripped.SkeletonRelativePath);

            // …and the planner's own validation accepts it, which is what the reported error was.
            ReferenceApplicationSelectionValidation.Validate(replanned);
        }
    }

    [Fact]
    public void Validation_RefusesAnAppImageBindingWithNoChecksum()    {
        var binding = new ReferenceApplicationSelection
        {
            ElementKey = "Location",
            Kind = nameof(ImageStepSlotKind.Location),
            SemanticRole = "location continuity",
            Strategy = ReferenceStrategyResolver.IdentityNativeMultiReference,
            Source = nameof(ImageStepReferenceSourceKind.AppImage),
            ReferenceImageId = "rendered-1"
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => ReferenceApplicationSelectionValidation.Validate([binding]));

        Assert.Contains("no checksum", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_RefusesABindingThatDeclaresAStrategyButNamesNothing()
    {
        var binding = new ReferenceApplicationSelection
        {
            ElementKey = "Location",
            Kind = nameof(ImageStepSlotKind.Location),
            SemanticRole = "location continuity",
            Strategy = "IdentityNativeMultiReference"
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => ReferenceApplicationSelectionValidation.Validate([binding]));

        Assert.Contains("names no reference image", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolver_ReadsARenderedImageByItsOwnIdAndChecksum()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedRenderedImageAsync("rendered-1", "SHA-rendered-1", named: "the trailer at dusk");

        var reference = await fixture.ResolveAsync(AppImageBinding("rendered-1", "SHA-rendered-1"));

        Assert.Equal(ImageStepSlotKind.Location, reference.SlotKind);
        // The operator's own name describes it, exactly as an approved asset's name does.
        Assert.Contains("the trailer at dusk", reference.Description, StringComparison.Ordinal);
        Assert.Equal("SHA-rendered-1", reference.Sha256);
        Assert.Equal("rendered-1.png", reference.FileName);
        await using var bytes = await reference.OpenAsync(CancellationToken.None);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public async Task Resolver_RefusesARenderedImageWhoseBytesChangedSinceItWasBound()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedRenderedImageAsync("rendered-1", "SHA-after-a-re-render");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.ResolveAsync(AppImageBinding("rendered-1", "SHA-as-bound")));

        Assert.Contains("no longer matches the checksum", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolver_RefusesARenderedImageThatIsNoLongerThere()
    {
        await using var fixture = await Fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.ResolveAsync(AppImageBinding("deleted-since", "SHA-deleted-since")));

        Assert.Contains("was not found", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repository_NamesARenderedImageAndReadsTheNameBack()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedRenderedImageAsync("rendered-1", "SHA-rendered-1");
        Assert.Null((await fixture.Images.GetImageAsync("rendered-1"))!.DisplayName);

        var named = await fixture.Images.SetDisplayNameAsync("rendered-1", "  the trailer at dusk  ", Now);

        Assert.Equal("the trailer at dusk", named.DisplayName);
        Assert.Equal("the trailer at dusk", (await fixture.Images.GetImageAsync("rendered-1"))!.DisplayName);

        // A blank name clears it rather than storing an empty string, so "unnamed" has one representation.
        var cleared = await fixture.Images.SetDisplayNameAsync("rendered-1", " ", Now);
        Assert.Null(cleared.DisplayName);
    }

    [Fact]
    public void Requests_ForTheSameImageAreDistinctPresses()
    {
        var first = SceneImageLocationReferenceRequest.ForRenderedImage("rendered-1", "SHA-1", "trailer");
        var second = SceneImageLocationReferenceRequest.ForRenderedImage("rendered-1", "SHA-1", "trailer");

        // The composer applies a request once per key, so two presses of the same button have to be two keys: keyed on
        // the image alone, "I cleared it and asked again" would be de-duplicated into doing nothing.
        Assert.NotEqual(first.Key, second.Key);

        // An approved asset image and a rendered one are different channels even when the ids are similar, so a request
        // for one can never be applied as the other.
        var asset = SceneImageLocationReferenceRequest.ForApprovedAssetImage("asset-1", "rendered-1", 3, "SHA-1");
        Assert.Equal(ImageStepReferenceSourceKind.ApprovedSceneAsset, asset.SourceKind);
        Assert.Equal("asset-1", asset.ToSlotSource("IdentityNativeMultiReference").SceneAssetId);
        Assert.Equal(3, asset.ToSlotSource("IdentityNativeMultiReference").SceneAssetVersion);
        Assert.Null(asset.ToSlotSource("IdentityNativeMultiReference").ReferenceImageId);
    }

    private static ReferenceApplicationSelection AppImageBinding(string imageId, string sha256)
        => new()
        {
            ElementKey = "Location",
            Kind = nameof(ImageStepSlotKind.Location),
            SemanticRole = "location continuity",
            // The name the GENERATE surface implements (a reference-conditioned render), which is what a composition is.
            Strategy = ReferenceStrategyResolver.IdentityNativeMultiReference,
            Source = nameof(ImageStepReferenceSourceKind.AppImage),
            ReferenceImageId = imageId,
            ReferenceImageSha256 = sha256
        };

    private sealed class Fixture : IAsyncDisposable
    {
        private const string ModelId = "editor-model";

        private Fixture(string databasePath)
        {
            DatabasePath = databasePath;
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={databasePath};Pooling=False"
            });
            Images = new SceneImageRepository(options);
            Assets = new SceneAssetRepository(options);
            Rustage = new ByteServingStorage();
            Resolver = new MediaEditReferenceResolver(
                Assets,
                new UnusedAssetStorage(),
                new QualifiedStrategies(),
                sceneImages: Images,
                sceneImageStorage: Rustage);
        }

        public string DatabasePath { get; }

        public SceneImageRepository Images { get; }

        public SceneAssetRepository Assets { get; }

        public ByteServingStorage Rustage { get; }

        public MediaEditReferenceResolver Resolver { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture(
                Path.Combine(Path.GetTempPath(), $"app-image-reference-{Guid.NewGuid():N}.db"));
            // Both repositories create their own schema on first use, so touching them once is what makes the store
            // ready to seed into.
            _ = await fixture.Images.GetImageAsync("schema-probe");
            _ = await fixture.Assets.GetAsync("schema-probe");
            return fixture;
        }

        public Task SeedRenderedImageAsync(string id, string sha256, string? named = null)
            => Images.InsertImageAsync(new SceneImageRecord
            {
                Id = id,
                SessionId = "session",
                InteractionId = "interaction",
                PromptRecordId = "prompt",
                PromptSnapshot = $"prompt-{id}",
                Status = SceneImageStatus.Complete,
                FileRelativePath = $"session/{id}.png",
                Sha256 = sha256,
                DisplayName = named,
                CreatedUtc = Now,
                UpdatedUtc = Now
            });

        public async Task<MediaEditReference> ResolveAsync(ReferenceApplicationSelection binding)
        {
            // The reference's own PNG is served under the path the row records, so "found but unreadable" cannot be
            // mistaken for "resolved".
            Rustage.Bytes[$"session/{binding.ReferenceImageId}.png"] = PngBytes;
            var resolved = await Resolver.ResolveAsync(
                ModelId, [binding], ReferenceStrategyCatalogue.ReferenceImageSurface.Generate);
            return Assert.Single(resolved);
        }

        public ValueTask DisposeAsync()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(DatabasePath))
            {
                File.Delete(DatabasePath);
            }

            return ValueTask.CompletedTask;
        }

        private static byte[] PngBytes =>
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
        ];
    }

    /// <summary>Serves the bytes the row records, which is what a reference read has to reach through the store.</summary>
    private sealed class ByteServingStorage : ISceneImageStorageService
    {
        public Dictionary<string, byte[]> Bytes { get; } = new(StringComparer.Ordinal);

        public Task<string> SaveAsync(string sessionId, string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => Bytes.TryGetValue(relativePath, out var bytes)
                ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
                : throw new InvalidOperationException($"No stored bytes at '{relativePath}'.");

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>The asset store is never reached by an app-image binding, so it refuses rather than silently serving.</summary>
    private sealed class UnusedAssetStorage : ISceneAssetStorageService
    {
        public Task<StoredSceneAsset> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class QualifiedStrategies : IReferenceStrategyResolver
    {
        public Task<ReferenceStrategyResolution> ResolveAsync(
            string registeredModelId, string strategy, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReferenceStrategyResolution(
                ReferenceStrategyResolutionStatus.Possible,
                strategy,
                Reason: string.Empty));
    }
}
