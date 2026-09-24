using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The body card and the body target's seeded configuration (B-122 Phase 0, Section A).
///
/// Two invariants are pinned here: an unanswered body card can never produce a prompt (the capture list is
/// explicit that a vague body description does not train, so an empty field is a missing decision, not
/// "none"), and a body card is saved under optimistic concurrency rather than letting two editors overwrite
/// each other.
/// </summary>
public sealed class CharacterBodyCardTests
{
    /// <summary>
    /// A decision record is not a descriptor (B-122, 2026-09-22): "none" means "the operator decided there is none",
    /// and pasting it into the positive prompt put the literal word in the render (the operator's body prompts
    /// contained "…, none, clean."). Absences are omitted; the completeness gate still requires the decision to be
    /// made, so nothing is silently skipped in review.
    /// </summary>
    [Fact]
    public void ToPromptLine_OmitsDecisionRecords_ButStillRequiresTheDecision()
    {
        var card = CompleteCard();
        card.ScarsMarks = "none";
        card.BodyHair = "N/A.";

        var line = card.ToPromptLine();

        Assert.DoesNotContain("none", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("n/a", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(", ,", line, StringComparison.Ordinal);
        // The renderable descriptors are still there, in canonical order.
        Assert.Contains("tattoo of a tree on left calf", line, StringComparison.Ordinal);
        Assert.Contains("curvy, full bust", line, StringComparison.Ordinal);
        // An unanswered field is still an unanswered decision, and rendering still refuses.
        card.PubicHair = "   ";
        Assert.False(card.IsReady);
        Assert.Throws<InvalidOperationException>(() => card.ToPromptLine());
    }

    [Fact]
    public void IsDecisionOnly_RecognisesTheDecisionVocabulary_NotDescriptions()
    {
        foreach (var decision in new[] { "", "  ", "none", "None", "N/A", "unknown", "not specified", "nil" })
        {
            Assert.True(CharacterBodyCard.IsDecisionOnly(decision), $"'{decision}' should read as a decision.");
        }

        foreach (var descriptor in new[] { "smooth, hairless", "small trimmed triangle", "no visible scars on the arms" })
        {
            Assert.False(CharacterBodyCard.IsDecisionOnly(descriptor), $"'{descriptor}' should render.");
        }
    }
    [Fact]
    public void FreshCard_ListsEveryFieldInCanonicalOrder_AndCannotProduceAPrompt()
    {
        var card = new CharacterBodyCard { CharacterTemplateId = "char-1" };

        Assert.False(card.IsReady);
        Assert.Equal(
            new[]
            {
                CharacterBodyCardField.BodyShape,
                CharacterBodyCardField.HeightBuild,
                CharacterBodyCardField.Skin,
                CharacterBodyCardField.BodyHair,
                CharacterBodyCardField.Tattoos,
                CharacterBodyCardField.ScarsMarks,
                CharacterBodyCardField.PubicHair
            },
            card.UnresolvedFields.Select(field => field.Field).ToArray());
    }

    [Fact]
    public void UnresolvedDecisions_AreTheCaptureListsDecideItems()
    {
        // The capture list names tattoo design/placement, body-hair pattern and pubic hair as [DECIDE].
        var card = new CharacterBodyCard
        {
            CharacterTemplateId = "char-1",
            BodyShape = "curvy",
            HeightBuild = "5'8\"",
            Skin = "fair",
            ScarsMarks = "none"
        };

        Assert.Equal(
            new[]
            {
                CharacterBodyCardField.BodyHair,
                CharacterBodyCardField.Tattoos,
                CharacterBodyCardField.PubicHair
            },
            card.UnresolvedDecisions.Select(field => field.Field).ToArray());
    }

    [Fact]
    public void RequireReadyForGeneration_NamesEveryUnansweredField_AndMarksTheDecisions()
    {
        var card = new CharacterBodyCard { CharacterTemplateId = "becky-1", BodyShape = "curvy" };

        var error = Assert.Throws<InvalidOperationException>(card.RequireReadyForGeneration);

        Assert.Contains("becky-1", error.Message, StringComparison.Ordinal);
        Assert.Contains("Height and build", error.Message, StringComparison.Ordinal);
        Assert.Contains("Body hair (chest, stomach, arms, legs) (a [DECIDE] item", error.Message, StringComparison.Ordinal);
        Assert.Contains("Tattoos (design + exact placement) (a [DECIDE] item", error.Message, StringComparison.Ordinal);
        Assert.Contains("Pubic hair (a [DECIDE] item", error.Message, StringComparison.Ordinal);
        Assert.Contains("Scars and marks", error.Message, StringComparison.Ordinal);
        // The one field that IS answered must not be reported as missing.
        Assert.DoesNotContain("Body shape and proportions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToPromptLine_IsTheCanonicalCardLine_InTheCaptureListsOrder()
    {
        var card = new CharacterBodyCard
        {
            CharacterTemplateId = "becky-1",
            BodyShape = "curvy, full bust, soft waist, wide hips",
            HeightBuild = "5'8\", medium build",
            Skin = "fair smooth skin",
            BodyHair = "moderate chest hair",
            Tattoos = "small flower on left forearm, butterfly on right ankle",
            ScarsMarks = "none",
            PubicHair = "neatly trimmed"
        };

        Assert.True(card.IsReady);
        // ScarsMarks is "none" — a decision the operator made, not something to draw. It is recorded on the card and
        // deliberately absent from the prompt line (2026-09-22: absences are omitted, never sent to the model).
        Assert.Equal(
            "curvy, full bust, soft waist, wide hips, 5'8\", medium build, fair smooth skin, "
            + "moderate chest hair, small flower on left forearm, butterfly on right ankle, neatly trimmed",
            card.ToPromptLine());
    }

    [Fact]
    public void ToPromptLine_RefusesToRenderWhileAnythingIsUnanswered()
    {
        var card = new CharacterBodyCard { CharacterTemplateId = "becky-1", BodyShape = "curvy" };

        var error = Assert.Throws<InvalidOperationException>(card.ToPromptLine);

        Assert.Contains("is incomplete", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_CreatesVersionOne_ThenIncrements_AndRoundTripsEveryField()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            var saved = await repository.SaveAsync(BeckyCard(), expectedVersion: 0);

            Assert.Equal(1, saved.Version);
            Assert.Equal("becky-1", saved.CharacterTemplateId);

            var read = await repository.GetAsync("becky-1");
            Assert.NotNull(read);
            Assert.Equal(BeckyCard().ToPromptLine(), read!.ToPromptLine());

            saved.BodyHair = "smooth";
            var updated = await repository.SaveAsync(saved, expectedVersion: 1);

            Assert.Equal(2, updated.Version);
            Assert.Equal("smooth", (await repository.GetAsync("becky-1"))!.BodyHair);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Save_WithAStaleVersion_RefusesToOverwriteAndSaysWhatChanged()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            await repository.SaveAsync(BeckyCard(), expectedVersion: 0);
            var second = await repository.SaveAsync(BeckyCard(), expectedVersion: 1);
            Assert.Equal(2, second.Version);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.SaveAsync(BeckyCard(), expectedVersion: 1));

            Assert.Contains("changed while it was being edited", error.Message, StringComparison.Ordinal);
            Assert.Contains("stored version is 2", error.Message, StringComparison.Ordinal);
            Assert.Contains("expected 1", error.Message, StringComparison.Ordinal);

            // The refused save left the stored card untouched.
            Assert.Equal("moderate chest hair", (await repository.GetAsync("becky-1"))!.BodyHair);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Save_CreateAgainstAnExistingCard_IsRefused_SoThereIsOnlyEverOneRow()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            await repository.SaveAsync(BeckyCard(), expectedVersion: 0);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.SaveAsync(BeckyCard(), expectedVersion: 0));

            Assert.Contains("already has a body card", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Save_UpdateWithoutACard_IsRefused_RatherThanSilentlyCreatingOne()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.SaveAsync(BeckyCard(), expectedVersion: 3));

            Assert.Contains("has no body card", error.Message, StringComparison.Ordinal);
            Assert.Null(await repository.GetAsync("becky-1"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Get_UnknownCharacter_ReturnsNull()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            Assert.Null(await repository.GetAsync("nobody"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// The card's picks compose into the one body-shape line: the axes in order, then the measurements under their
    /// short labels — the shape the BodyShape line already had before the axes existed.
    /// </summary>
    [Fact]
    public void Axes_Compose_JoinsTheAxesInOrder_ThenTheLabelledMeasurements()
    {
        var axes = new CharacterBodyAxes
        {
            BodyBuild = "average frame",
            Adiposity = "average weight",
            FatDistribution = "fuller rear with a soft belly",
            BustSize = "Full",
            HipSize = "Wide",
            ButtSize = "full"
        };

        Assert.False(axes.IsEmpty);
        Assert.Equal(
            "average frame, average weight, fuller rear with a soft belly, bust Full, hips Wide, rear full",
            axes.Compose());
    }

    /// <summary>An unpicked axis is omitted from the composed line — never defaulted, and never a stray separator.</summary>
    [Fact]
    public void Axes_Compose_OmitsEveryUnsetAxis()
    {
        var empty = new CharacterBodyAxes();
        Assert.True(empty.IsEmpty);
        Assert.Equal(string.Empty, empty.Compose());

        var onePick = new CharacterBodyAxes { Silhouette = "  pear  " };
        Assert.False(onePick.IsEmpty);
        Assert.Equal("pear", onePick.Compose());
    }

    /// <summary>
    /// Picking body parts does NOT answer a card field: the completeness gate stays exactly the seven fields, so the
    /// axes can never make an undescribed body look ready for generation.
    /// </summary>
    [Fact]
    public void Axes_AreNotAGatedField_SoTheCompletenessContractIsUnchanged()
    {
        var blank = new CharacterBodyCard { CharacterTemplateId = "char-1" };
        blank.Axes.Adiposity = "average weight";
        blank.Axes.FatDistribution = "fuller rear with a soft belly";

        Assert.False(blank.IsReady);
        Assert.Equal(CharacterBodyCardFields.All.Count, blank.UnresolvedFields.Count);
    }

    [Fact]
    public async Task Save_RoundTripsTheAxisPicks()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            var card = BeckyCard();
            card.Axes.Adiposity = "average weight";
            card.Axes.FatDistribution = "fuller rear with a soft belly";

            await repository.SaveAsync(card, expectedVersion: 0);
            var read = await repository.GetAsync("becky-1");

            Assert.NotNull(read);
            Assert.Equal("average weight", read!.Axes.Adiposity);
            Assert.Equal("fuller rear with a soft belly", read.Axes.FatDistribution);
            // An unpicked axis comes back unset rather than as something invented.
            Assert.Null(read.Axes.Silhouette);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task BodyTemplates_AreSeededIntoTheOneTemplateStore_AndResolveByKey()
    {
        var (service, _, dbPath) = CreateTemplateService();
        try
        {
            foreach (var key in CharacterBodyWorkflowKeys.All)
            {
                var resolved = await service.ResolveAsync(key, null);
                Assert.Equal(key, resolved.Key);
                Assert.Equal(ImageWorkflowPromptTemplateScope.Global, resolved.Scope);
                Assert.False(string.IsNullOrWhiteSpace(resolved.Body));
                Assert.Equal(resolved.Body, resolved.SeedBody);
            }

            // The card line is a placeholder the handler fills from the character's body card.
            var normalize = await service.ResolveAsync(CharacterBodyWorkflowKeys.Normalize, null);
            Assert.Contains("{BodyCard}", normalize.Body, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task BodyTemplate_CharacterOverrideAndReset_BehaveLikeEveryOtherKey()
    {
        var (service, _, dbPath) = CreateTemplateService();
        try
        {
            await service.SaveTemplateAsync(new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.ClothedAcquire,
                Scope = ImageWorkflowPromptTemplateScope.Character,
                CharacterProfileId = "becky-1",
                WorkflowStep = "BodyClothedBase",
                Body = "BECKY OVERRIDE",
                SeedBody = "BECKY OVERRIDE"
            });

            var overridden = await service.ResolveAsync(CharacterBodyWorkflowKeys.ClothedAcquire, "becky-1");
            Assert.Equal("BECKY OVERRIDE", overridden.Body);
            Assert.Equal(ImageWorkflowPromptTemplateScope.Character, overridden.Scope);

            var other = await service.ResolveAsync(CharacterBodyWorkflowKeys.ClothedAcquire, "someone-else");
            Assert.NotEqual("BECKY OVERRIDE", other.Body);

            var reset = await service.ResetToSeedAsync(
                CharacterBodyWorkflowKeys.ClothedAcquire,
                ImageWorkflowPromptTemplateScope.Character,
                "becky-1");

            Assert.Equal(reset.SeedBody, reset.Body);
            Assert.NotEqual("BECKY OVERRIDE", reset.Body);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task BodyTemplates_ResolveIndependentlyOfTheFaceKeys()
    {
        var (service, _, dbPath) = CreateTemplateService();
        try
        {
            // Both namespaces coexist in the one store: the body target does not displace the face keys.
            var face = await service.ResolveAsync("identity.front.generate", null);
            var body = await service.ResolveAsync(CharacterBodyWorkflowKeys.ClothedAcquire, null);

            Assert.NotEqual(face.Body, body.Body);
            Assert.Contains("Head and shoulders", face.Body, StringComparison.Ordinal);
            Assert.Contains("head to feet", body.Body, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task BodyModelId_RoundTrips_AndIsUnsetInTheSeed_SoNoModelIsAssumed()
    {
        var (_, repository, dbPath) = CreateTemplateService();
        try
        {
            var seeded = await repository.GetSettingsAsync("global");
            Assert.NotNull(seeded);
            Assert.Null(seeded!.BodyModelId);

            seeded.BodyModelId = "model-body-1";
            await repository.UpsertSettingsAsync(seeded);

            Assert.Equal("model-body-1", (await repository.GetSettingsAsync("global"))!.BodyModelId);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static CharacterBodyCard BeckyCard() => new()
    {
        CharacterTemplateId = "becky-1",
        BodyShape = "curvy, full bust, soft waist, wide hips",
        HeightBuild = "5'8\", medium build",
        Skin = "fair smooth skin",
        BodyHair = "moderate chest hair",
        Tattoos = "small flower on left forearm, butterfly on right ankle",
        ScarsMarks = "none",
        PubicHair = "neatly trimmed"
    };

    /// <summary>A card with every field answered, used by the prompt-line tests.</summary>
    private static CharacterBodyCard CompleteCard() => new()
    {
        CharacterTemplateId = "becky-1",
        BodyShape = "curvy, full bust, soft waist, wide hips",
        HeightBuild = "5'8\", medium build",
        Skin = "fair smooth skin",
        BodyHair = "moderate chest hair",
        Tattoos = "tattoo of a tree on left calf",
        ScarsMarks = "small mole on the left cheek",
        PubicHair = "neatly trimmed"
    };

    private static (CharacterBodyCardRepository repository, string dbPath) CreateRepository()
    {
        var dbPath = TempDbPath();
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repository = new CharacterBodyCardRepository(options);
        repository.EnsureSchemaAsync().GetAwaiter().GetResult();
        return (repository, dbPath);
    }

    private static (ImageWorkflowTemplateService service, ImageWorkflowRepository repository, string dbPath) CreateTemplateService()
    {
        var dbPath = TempDbPath();
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repository = new ImageWorkflowRepository(options);
        repository.EnsureSchemaAsync().GetAwaiter().GetResult();
        return (new ImageWorkflowTemplateService(repository), repository, dbPath);
    }

    private static string TempDbPath()
        => Path.Combine(Path.GetTempPath(), $"body-card-{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
