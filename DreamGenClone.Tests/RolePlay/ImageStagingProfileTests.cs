using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-157 — the staging-profile store and its validation contract.
///
/// <para>
/// The point of these tests is the posture, not the happy path: a profile that cannot say where a subject sits, what
/// camera to fall back to, or which surface it governs is REFUSED by name. Nothing is repaired and nothing is
/// defaulted, because a repaired profile places subjects the operator never chose and produces an image that is
/// indistinguishable from one that honoured a real configuration.
/// </para>
/// </summary>
public sealed class ImageStagingProfileTests
{
    private static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"b157-staging-{Guid.NewGuid():N}.db");

    private static ImageStagingProfileRepository NewRepository(string dbPath) =>
        new(Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" }));

    private static ImageStagingProfile ValidProfile() => new()
    {
        Id = "staging-test",
        DisplayName = "Test staging",
        Surface = ImageStagingSurface.Composition,
        PlacementLadderJson = """["Center","Left","Right"]""",
        ActiveDepth = FrameDepth.Foreground,
        ObserverDepth = FrameDepth.Background,
        DefaultCameraAngle = "eye level",
        DefaultShotSize = "medium shot",
        OmniscientShotSize = "wide establishing shot",
        DefaultCameraHeight = CameraHeights.Eye,
        CameraKeywordMapJson = """[{"keyword":"wide","shotSize":"wide establishing shot"}]""",
        Version = 2
    };

    [Fact]
    public void Validate_AcceptsACompleteProfile()
    {
        ImageStagingProfileValidation.Validate(ValidProfile());
    }

    [Fact]
    public void Validate_RefusesAnUnnamedProfile()
    {
        var profile = ValidProfile();
        profile.DisplayName = "  ";

        var error = Assert.Throws<InvalidOperationException>(() => ImageStagingProfileValidation.Validate(profile));
        Assert.Contains("display name", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RefusesAnUnknownSurface()
    {
        var profile = ValidProfile();
        profile.Surface = ImageStagingSurface.Unknown;

        var error = Assert.Throws<InvalidOperationException>(() => ImageStagingProfileValidation.Validate(profile));
        Assert.Contains("surface", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_RefusesAnUnknownDepth(bool active)
    {
        var profile = ValidProfile();
        if (active)
        {
            profile.ActiveDepth = FrameDepth.Unknown;
        }
        else
        {
            profile.ObserverDepth = FrameDepth.Unknown;
        }

        var error = Assert.Throws<InvalidOperationException>(() => ImageStagingProfileValidation.Validate(profile));
        Assert.Contains("depth", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RefusesAnEmptyPlacementLadder()
    {
        var profile = ValidProfile();
        profile.PlacementLadderJson = "[]";

        var error = Assert.Throws<InvalidOperationException>(() => ImageStagingProfileValidation.Validate(profile));
        Assert.Contains("placement ladder", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RefusesAPlacementThatIsNotAPosition()
    {
        var profile = ValidProfile();
        profile.PlacementLadderJson = """["Center","Sideways"]""";

        var error = Assert.Throws<InvalidOperationException>(() => ImageStagingProfileValidation.Validate(profile));
        Assert.Contains("Sideways", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RefusesARepeatedPlacement()
    {
        // Two subjects sharing one position is precisely the collision B-157 exists to remove, so a ladder that
        // could produce it is refused rather than allowed to double subjects up.
        var profile = ValidProfile();
        profile.PlacementLadderJson = """["Center","Center"]""";

        var error = Assert.Throws<InvalidOperationException>(() => ImageStagingProfileValidation.Validate(profile));
        Assert.Contains("more than once", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RefusesAnUnsetCameraFallback()
    {
        var profile = ValidProfile();
        profile.DefaultShotSize = string.Empty;

        var error = Assert.Throws<InvalidOperationException>(() => ImageStagingProfileValidation.Validate(profile));
        Assert.Contains("shot size", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RefusesAKeywordThatChangesNothing()
    {
        var profile = ValidProfile();
        profile.CameraKeywordMapJson = """[{"keyword":"wide"}]""";

        var error = Assert.Throws<InvalidOperationException>(() => ImageStagingProfileValidation.Validate(profile));
        Assert.Contains("wide", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadPlacementLadder_ReturnsTheDeclaredOrder()
    {
        var ladder = ImageStagingProfileValidation.ReadPlacementLadder(ValidProfile());

        Assert.Equal(new[] { FrameHorizontal.Center, FrameHorizontal.Left, FrameHorizontal.Right }, ladder);
    }

    [Fact]
    public void ReadCameraKeywordMap_ReadsAngleShotSizeAndSide()
    {
        var profile = ValidProfile();
        profile.CameraKeywordMapJson =
            """[{"keyword":"wide","shotSize":"wide establishing shot"},{"keyword":"over","angle":"over-the-shoulder","side":"from behind"}]""";

        var keywords = ImageStagingProfileValidation.ReadCameraKeywordMap(profile);

        Assert.Equal(2, keywords.Count);
        Assert.Equal("wide establishing shot", keywords[0].ShotSize);
        Assert.Null(keywords[0].Angle);
        Assert.Equal("over-the-shoulder", keywords[1].Angle);
        Assert.Equal("from behind", keywords[1].Side);
    }

    /// <summary>
    /// B-161: the framings a Composer may be rendered at come from THE PROFILE — its default shot size first, then the
    /// distinct shot sizes its camera keyword map declares.
    ///
    /// <para>
    /// This is the Composer's framing control's whole vocabulary, which is why it is read here rather than listed in a
    /// page: a framing an operator can pick has to be one this surface can honour, and a new one is added on /profiles
    /// where every other staging decision lives. Deduplicated case-insensitively, because a keyword map legitimately
    /// maps two keywords to one shot size.
    /// </para>
    /// </summary>
    [Fact]
    public void ReadFramingChoices_OffersTheDefaultsShotSizeThenTheMapsDistinctOnes()
    {
        var profile = ValidProfile();
        profile.DefaultShotSize = "medium shot";
        profile.CameraKeywordMapJson =
            """
            [{"keyword":"wide","shotSize":"wide establishing shot"},
             {"keyword":"wide shot","shotSize":"Wide Establishing Shot"},
             {"keyword":"close","shotSize":"close shot"},
             {"keyword":"low","angle":"low angle"}]
            """;

        var choices = ImageStagingProfileValidation.ReadFramingChoices(profile);

        // The default leads, then each distinct shot size in configured order — and an angle-only keyword contributes
        // no framing, because it declares no shot size to offer.
        Assert.Equal(new[] { "medium shot", "wide establishing shot", "close shot" }, choices);
    }

    /// <summary>
    /// A profile that declares no shot size anywhere yields NO choices rather than an invented one: the Composer's
    /// framing control then does not render, and the render keeps the framing the brief was compiled with.
    /// </summary>
    [Fact]
    public void ReadFramingChoices_YieldsNothingWhenTheProfileDeclaresNoShotSize()
    {
        var profile = ValidProfile();
        profile.DefaultShotSize = string.Empty;
        profile.OmniscientShotSize = string.Empty;
        profile.CameraKeywordMapJson = """[{"keyword":"low","angle":"low angle"}]""";

        Assert.Empty(ImageStagingProfileValidation.ReadFramingChoices(profile));
    }

    /// <summary>
    /// B-166 D7: the omniscient lean is required configuration. A profile that declares none is refused rather than
    /// silently framed with the composition default, which is indistinguishable from choosing it deliberately.
    /// </summary>
    [Fact]
    public void Validate_RefusesAnUnsetOmniscientShotSize()
    {
        var profile = ValidProfile();
        profile.OmniscientShotSize = "   ";

        var error = Assert.Throws<InvalidOperationException>(() => ImageStagingProfileValidation.Validate(profile));
        Assert.Contains("omniscient shot size", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The omniscient framing is part of the Composer's framing vocabulary, so an operator can select it explicitly.
    /// </summary>
    [Fact]
    public void ReadFramingChoices_IncludesTheOmniscientShotSize()
    {
        var profile = ValidProfile();
        profile.DefaultShotSize = "medium shot";
        profile.OmniscientShotSize = "wide establishing shot";
        profile.CameraKeywordMapJson = "[]";

        var choices = ImageStagingProfileValidation.ReadFramingChoices(profile);

        Assert.Equal(new[] { "medium shot", "wide establishing shot" }, choices);
    }

    [Fact]
    public async Task Repository_SeedsOneProfilePerSurface()
    {
        var repository = NewRepository(NewDbPath());

        var profiles = await repository.ListAsync();

        Assert.Equal(
            new[] { ImageStagingSurface.Composition, ImageStagingSurface.AssetManager },
            profiles.Select(profile => profile.Surface).OrderBy(surface => surface));
        Assert.All(profiles, profile => ImageStagingProfileValidation.Validate(profile));
    }

    [Fact]
    public async Task Repository_ResolvesEachSeededSurface()
    {
        var repository = NewRepository(NewDbPath());

        var composition = await repository.FindBySurfaceAsync(ImageStagingSurface.Composition);
        var assetManager = await repository.FindBySurfaceAsync(ImageStagingSurface.AssetManager);

        Assert.NotNull(composition);
        Assert.NotNull(assetManager);
        Assert.Equal(FrameHorizontal.Center, ImageStagingProfileValidation.ReadPlacementLadder(composition!)[0]);
        Assert.Equal(FrameDepth.Foreground, composition!.ActiveDepth);
    }

    [Fact]
    public async Task Repository_IsIdempotentAcrossOpens()
    {
        var dbPath = NewDbPath();
        var first = NewRepository(dbPath);
        await first.ListAsync();

        var second = NewRepository(dbPath);
        var profiles = await second.ListAsync();

        Assert.Equal(2, profiles.Count);
    }

    [Fact]
    public async Task Repository_DoesNotRevertAnEditedRow()
    {
        // A staging profile is editable configuration. The seed must never restore the shipped value over an
        // operator's change — that would silently re-place every subject on the next app start.
        var dbPath = NewDbPath();
        var repository = NewRepository(dbPath);
        var edited = await repository.FindBySurfaceAsync(ImageStagingSurface.Composition);
        Assert.NotNull(edited);
        edited!.PlacementLadderJson = """["Left","Right"]""";
        edited.DefaultShotSize = "operator shot";
        await repository.UpsertAsync(edited);

        var reopened = NewRepository(dbPath);
        var reloaded = await reopened.FindBySurfaceAsync(ImageStagingSurface.Composition);

        Assert.NotNull(reloaded);
        Assert.Equal("""["Left","Right"]""", reloaded!.PlacementLadderJson);
        Assert.Equal("operator shot", reloaded.DefaultShotSize);
    }

    [Fact]
    public async Task Repository_RefusesTheUnknownSurface()
    {
        var repository = NewRepository(NewDbPath());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.FindBySurfaceAsync(ImageStagingSurface.Unknown));

        Assert.Contains("Unknown", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repository_RefusesAnInvalidWrite()
    {
        var repository = NewRepository(NewDbPath());
        var profile = ValidProfile();
        profile.PlacementLadderJson = "[]";

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertAsync(profile));
    }
}
