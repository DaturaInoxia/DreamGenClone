using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 B135-001/002 — the per-checkpoint compiler profile store.
///
/// <para>
/// Two things are being pinned here. First, the validation contract: a profile with an uncited negative, an
/// impossible family/dialect pair, an unknown pose capability or an inconsistent budget is REFUSED, never repaired.
/// Second, the seed's coverage and its negative posture at the data level, because "negatives keep coming back" is
/// the recurring defect this store is meant to end.
/// </para>
/// </summary>
public sealed class ImageCompilerProfileTests
{
    /// <summary>
    /// The checkpoint identifiers actually registered in Model Manager, verified against the dev DB on 2026-09-29.
    /// If a seed row is dropped, the checkpoint it covered becomes un-compilable, so this is a regression guard
    /// rather than a snapshot: adding a model does not fail it, losing a profile does.
    /// </summary>
    private static readonly string[] RegisteredImageCheckpoints =
    [
        "ponyRealism_V23ULTRA.safetensors",
        "ponyDiffusionV6XL_v6.safetensors",
        "bigLust_v16.safetensors",
        "juggernautXL_ragnarok.safetensors",
        "flux1-dev-fp8.safetensors",
        "qwen_image_2.1_int8_convrot.safetensors",
        "black-forest-labs/FLUX.2-pro",
        "ByteDance-Seed/Seedream-4.0",
        "black-forest-labs/FLUX.1.1-pro",
        "google/flash-image-3.1",
        "Qwen/Qwen-Image-2.0-Pro",
        "google/imagen-4.0-preview",
        "openai/gpt-image-2",
    ];

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"b135-profile-{Guid.NewGuid():N}.db");

    private static ImageCompilerProfileRepository NewRepository(string dbPath) =>
        new(Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" }));

    private static ImageCompilerProfile ValidProfile() => new()
    {
        Id = "profile-test",
        CheckpointIdentifier = "test-checkpoint.safetensors",
        DisplayName = "Test checkpoint",
        Family = SceneImageModelFamily.Sdxl,
        PromptDialect = SceneImagePromptDialect.SdxlNaturalLanguage,
        MinChars = 100,
        MaxChars = 600,
        MaxTokens = 75,
        PoseInText = ImagePoseInText.Forbidden,
        Negative = string.Empty,
        SettingsEnvelopeJson = "{}",
        ResearchSource = "test",
    };

    // ---- the validation contract -------------------------------------------------------------------------

    [Fact]
    public void Validate_AnUncitedNegative_IsRefused()
    {
        var profile = ValidProfile();
        profile.Negative = "lowres, bad anatomy";

        var error = Assert.Throws<InvalidOperationException>(() => ImageCompilerProfileValidation.Validate(profile));

        Assert.Contains("no source", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ACitedNegative_IsAccepted()
    {
        var profile = ValidProfile();
        profile.Family = SceneImageModelFamily.Pony;
        profile.PromptDialect = SceneImagePromptDialect.PonyV6Tags;
        profile.Negative = "lowres, bad anatomy";
        profile.NegativeSource = "pony-v6-prompting.instructions.md rules 8-9";

        ImageCompilerProfileValidation.Validate(profile);
    }

    [Fact]
    public void Validate_AnIncompatibleFamilyAndDialect_IsRefused()
    {
        var profile = ValidProfile();
        profile.Family = SceneImageModelFamily.Pony;
        profile.PromptDialect = SceneImagePromptDialect.SdxlNaturalLanguage;

        var error = Assert.Throws<InvalidOperationException>(() => ImageCompilerProfileValidation.Validate(profile));

        Assert.Contains("not a valid combination", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_AnUnknownPoseCapability_IsRefused()
    {
        var profile = ValidProfile();
        profile.PoseInText = ImagePoseInText.Unknown;

        var error = Assert.Throws<InvalidOperationException>(() => ImageCompilerProfileValidation.Validate(profile));

        Assert.Contains("pose", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_AnInconsistentBudget_IsRefused()
    {
        var profile = ValidProfile();
        profile.MinChars = 600;
        profile.MaxChars = 600;

        var error = Assert.Throws<InvalidOperationException>(() => ImageCompilerProfileValidation.Validate(profile));

        Assert.Contains("character budget", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_AMissingCheckpoint_IsRefused()
    {
        var profile = ValidProfile();
        profile.CheckpointIdentifier = "  ";

        var error = Assert.Throws<InvalidOperationException>(() => ImageCompilerProfileValidation.Validate(profile));

        Assert.Contains("checkpoint", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ANonArrayForbiddenList_IsRefused()
    {
        var profile = ValidProfile();
        profile.ForbiddenTokensJson = """{"not":"an array"}""";

        var error = Assert.Throws<InvalidOperationException>(() => ImageCompilerProfileValidation.Validate(profile));

        Assert.Contains("ForbiddenTokensJson", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the seeded store ---------------------------------------------------------------------------------

    [Fact]
    public async Task Seed_ResolvesEveryRegisteredImageCheckpoint()
    {
        var repository = NewRepository(NewDbPath());

        foreach (var checkpoint in RegisteredImageCheckpoints)
        {
            var profile = await repository.FindByCheckpointAsync(checkpoint);
            Assert.True(profile is not null, $"No compiler profile was seeded for checkpoint '{checkpoint}'.");
        }
    }

    [Fact]
    public async Task FindByCheckpoint_IsCaseInsensitive_AndReturnsNullWhenUnknown()
    {
        var repository = NewRepository(NewDbPath());

        var found = await repository.FindByCheckpointAsync("BIGLUST_V16.SAFETENSORS");
        Assert.NotNull(found);
        Assert.Equal("BigLust v1.6", found!.DisplayName);

        // The store does not invent a profile; the CALLER fails fast naming the checkpoint.
        Assert.Null(await repository.FindByCheckpointAsync("not-a-real-checkpoint.safetensors"));
    }

    [Fact]
    public async Task Seed_KeepsOneProfilePerCheckpoint_EvenWhenTwoModelsShareIt()
    {
        var repository = NewRepository(NewDbPath());

        var profiles = await repository.ListAsync();
        var bigLust = profiles.Where(p => string.Equals(p.CheckpointIdentifier, "bigLust_v16.safetensors", StringComparison.OrdinalIgnoreCase)).ToList();

        // BigLust is registered twice (local ComfyUI + serverless) against one checkpoint, so one profile serves both.
        Assert.Single(bigLust);
    }

    [Fact]
    public async Task Seed_DeclaresNoNegativeOutsideTheCitedPonyRows()
    {
        var repository = NewRepository(NewDbPath());

        var profiles = await repository.ListAsync();
        var withNegative = profiles.Where(p => !string.IsNullOrWhiteSpace(p.Negative)).ToList();

        Assert.NotEmpty(withNegative);
        Assert.All(withNegative, profile =>
        {
            Assert.Equal(SceneImageModelFamily.Pony, profile.Family);
            Assert.False(string.IsNullOrWhiteSpace(profile.NegativeSource),
                $"Profile '{profile.CheckpointIdentifier}' declares a negative with no citation.");
        });

        // Every non-Pony checkpoint resolves to an empty negative — the posture the 2026-09-08 research established,
        // now enforced as data rather than as scattered constants.
        Assert.All(
            profiles.Where(p => p.Family != SceneImageModelFamily.Pony),
            profile => Assert.Equal(string.Empty, profile.Negative));
    }

    [Fact]
    public async Task Seed_RefusesTheSdxlFamilyComplexPoseInText()
    {
        var repository = NewRepository(NewDbPath());

        var bigLust = await repository.FindByCheckpointAsync("bigLust_v16.safetensors");
        var juggernaut = await repository.FindByCheckpointAsync("juggernautXL_ragnarok.safetensors");
        var qwen = await repository.FindByCheckpointAsync("qwen_image_2.1_int8_convrot.safetensors");

        Assert.Equal(ImagePoseInText.Forbidden, bigLust!.PoseInText);
        Assert.Equal(ImagePoseInText.Forbidden, juggernaut!.PoseInText);
        Assert.Equal(ImagePoseInText.Full, qwen!.PoseInText);
    }

    [Fact]
    public async Task Seed_GivesQwenALongerBudgetThanTheSdxlFamily()
    {
        var repository = NewRepository(NewDbPath());

        var bigLust = await repository.FindByCheckpointAsync("bigLust_v16.safetensors");
        var qwen = await repository.FindByCheckpointAsync("qwen_image_2.1_int8_convrot.safetensors");

        Assert.True(qwen!.MaxChars > bigLust!.MaxChars,
            "Qwen-Image-2.1 must carry a larger prompt budget than the SDXL-family checkpoints.");
    }

    [Fact]
    public async Task Seed_IsIdempotent_AndNeverRevertsAnEditedRow()
    {
        var dbPath = NewDbPath();
        var first = NewRepository(dbPath);

        var edited = await first.FindByCheckpointAsync("bigLust_v16.safetensors");
        Assert.NotNull(edited);
        edited!.MaxChars = 4210;
        edited.Negative = string.Empty;
        edited.ResearchSource = "edited by the operator";
        await first.UpsertAsync(edited);

        // A second repository over the same database opens and re-runs the seed.
        var second = NewRepository(dbPath);
        var reloaded = await second.FindByCheckpointAsync("bigLust_v16.safetensors");

        Assert.Equal(4210, reloaded!.MaxChars);
        Assert.Equal("edited by the operator", reloaded.ResearchSource);
    }

    [Fact]
    public async Task Upsert_RefusesAnInvalidProfileWithoutWriting()
    {
        var dbPath = NewDbPath();
        var repository = NewRepository(dbPath);

        var profile = await repository.FindByCheckpointAsync("bigLust_v16.safetensors");
        profile!.Negative = "deformed, bad anatomy";

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertAsync(profile));

        var reloaded = await NewRepository(dbPath).FindByCheckpointAsync("bigLust_v16.safetensors");
        Assert.Equal(string.Empty, reloaded!.Negative);
    }

    [Fact]
    public async Task Repository_RequiresACheckpointIdentifier()
    {
        var repository = NewRepository(NewDbPath());

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.FindByCheckpointAsync("   "));
    }
}
