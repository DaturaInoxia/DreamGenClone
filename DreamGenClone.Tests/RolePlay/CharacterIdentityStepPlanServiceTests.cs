using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-121 B121-011a: a target kind's pipeline is persisted data (steps, order, handler, prompt template), and
/// the read is the only place that can catch a plan the app cannot run. These tests hold that boundary: the
/// seeded Face plan is the pipeline that exists, and every way a plan can be wrong fails fast naming what is
/// wrong instead of letting a build walk a step nothing implements.
/// </summary>
public sealed class CharacterIdentityStepPlanServiceTests
{
    /// <summary>
    /// A target kind the app does not ship. Since B-122 Phase 0 the Body kind HAS a seeded plan, so the tests
    /// that deliberately write a plan the app cannot run (or none at all) use this kind, which keeps them
    /// independent of which kinds ship.
    /// </summary>
    private static readonly CharacterIdentityTargetKind UnshippedKind = (CharacterIdentityTargetKind)99;

    [Fact]
    public async Task FacePlan_IsSeededInPipelineOrderWithItsHandlersAndTemplateKeys()
    {
        using var fixture = await Fixture.CreateAsync();

        var plan = await fixture.Service.GetPlanAsync(CharacterIdentityTargetKind.Face);

        Assert.Equal(
            new[]
            {
                CharacterIdentityBuildStep.Front,
                CharacterIdentityBuildStep.Validate,
                CharacterIdentityBuildStep.GarmentRemoval,
                CharacterIdentityBuildStep.Crop,
                CharacterIdentityBuildStep.Enhance,
                CharacterIdentityBuildStep.Angles,
                CharacterIdentityBuildStep.Promote
            },
            plan.Ordered.Select(definition => definition.Step).ToArray());

        Assert.Equal(CharacterIdentityBuildStep.Promote, plan.TerminalStep);
        Assert.Equal(CharacterIdentityBuildHandlers.Front, plan.Require(CharacterIdentityBuildStep.Front).HandlerKey);
        Assert.Equal(
            CharacterIdentityBuildHandlers.ValidateEye,
            plan.Require(CharacterIdentityBuildStep.Validate).HandlerKey);
        Assert.Equal(
            CharacterIdentityBuildHandlers.PromoteFacePack,
            plan.Require(CharacterIdentityBuildStep.Promote).HandlerKey);

        // The prompt keys the handlers resolve are the plan's, and the rows already in the database keep the
        // key names the seeded templates use.
        Assert.Equal("identity.front.generate", plan.RequireTemplateKey(CharacterIdentityBuildStep.Front));
        Assert.Equal("identity.garment.remove", plan.RequireTemplateKey(CharacterIdentityBuildStep.GarmentRemoval));
    }

    /// <summary>
    /// B-122 Phase 0 (B122-005): the body target is a plan in the same store — acquire the base, validate the
    /// base, one requested view, validate the views, promote — naming the app's body handlers. Nothing about the
    /// face pipeline changes, and the plan carries no face-only steps.
    /// </summary>
    [Fact]
    public async Task BodyPlan_IsSeededWithThePhaseZeroStepSetAndHandlers()
    {
        using var fixture = await Fixture.CreateAsync();

        var plan = await fixture.Service.GetPlanAsync(CharacterIdentityTargetKind.Body);

        Assert.Equal(
            new[]
            {
                CharacterIdentityBuildStep.Front,
                CharacterIdentityBuildStep.Validate,
                CharacterIdentityBuildStep.Angles,
                CharacterIdentityBuildStep.ValidateView,
                CharacterIdentityBuildStep.Promote
            },
            plan.Ordered.Select(definition => definition.Step).ToArray());

        Assert.Equal(CharacterIdentityBuildStep.Promote, plan.TerminalStep);
        Assert.False(plan.Contains(CharacterIdentityBuildStep.GarmentRemoval));
        Assert.False(plan.Contains(CharacterIdentityBuildStep.Crop));
        Assert.False(plan.Contains(CharacterIdentityBuildStep.Enhance));

        Assert.Equal(CharacterIdentityBuildHandlers.FrontBody, plan.Require(CharacterIdentityBuildStep.Front).HandlerKey);
        Assert.Equal(
            CharacterIdentityBuildHandlers.ValidateBodyBase,
            plan.Require(CharacterIdentityBuildStep.Validate).HandlerKey);
        Assert.Equal(CharacterIdentityBuildHandlers.AngleBody, plan.Require(CharacterIdentityBuildStep.Angles).HandlerKey);
        Assert.Equal(
            CharacterIdentityBuildHandlers.ValidateBodyView,
            plan.Require(CharacterIdentityBuildStep.ValidateView).HandlerKey);
        Assert.Equal(
            CharacterIdentityBuildHandlers.PromoteBodyPack,
            plan.Require(CharacterIdentityBuildStep.Promote).HandlerKey);

        // The base acquisition resolves the body's owned prompt key; the view step resolves a key per request.
        Assert.Equal(
            CharacterBodyWorkflowKeys.ClothedAcquire,
            plan.RequireTemplateKey(CharacterIdentityBuildStep.Front));
        var viewKeyError = Assert.Throws<InvalidOperationException>(
            () => plan.RequireTemplateKey(CharacterIdentityBuildStep.Angles));
        Assert.Contains("no prompt template", viewKeyError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnseededKind_FailsFastNamingTheKind()
    {
        using var fixture = await Fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.GetPlanAsync(UnshippedKind));

        Assert.Contains("99", error.Message, StringComparison.Ordinal);
        Assert.Contains("No character identity step plan is seeded", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownHandler_FailsFastNamingTheHandlerAndTheStep()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.InsertPlanRowAsync(
            UnshippedKind, order: 1, CharacterIdentityBuildStep.Angles, "angle.body.unknown");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.GetPlanAsync(UnshippedKind));

        Assert.Contains("angle.body.unknown", error.Message, StringComparison.Ordinal);
        Assert.Contains("Angles", error.Message, StringComparison.Ordinal);
        Assert.Contains("99", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AmbiguousOrder_FailsFast()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.InsertPlanRowAsync(
            UnshippedKind, order: 1, CharacterIdentityBuildStep.Front, CharacterIdentityBuildHandlers.Front);
        await fixture.InsertPlanRowAsync(
            UnshippedKind, order: 1, CharacterIdentityBuildStep.Angles, CharacterIdentityBuildHandlers.AngleFace);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.GetPlanAsync(UnshippedKind));

        Assert.Contains("same order", error.Message, StringComparison.Ordinal);
        Assert.Contains("99", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StepOutsideThePlan_FailsFastNamingTheStepAndTheKind()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.InsertPlanRowAsync(
            UnshippedKind, order: 1, CharacterIdentityBuildStep.Front, CharacterIdentityBuildHandlers.Front);
        var plan = await fixture.Service.GetPlanAsync(UnshippedKind);

        var stepError = Assert.Throws<InvalidOperationException>(
            () => plan.Require(CharacterIdentityBuildStep.Crop));
        Assert.Contains("Crop", stepError.Message, StringComparison.Ordinal);
        Assert.Contains("99", stepError.Message, StringComparison.Ordinal);

        // A step whose handler has no single template (the eye gate) says so rather than returning an empty key.
        var keyError = Assert.Throws<InvalidOperationException>(
            () => plan.RequireTemplateKey(CharacterIdentityBuildStep.Front));
        Assert.Contains("no prompt template", keyError.Message, StringComparison.Ordinal);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _dbPath;

        private Fixture(string dbPath, CharacterIdentityStepPlanService service)
        {
            _dbPath = dbPath;
            Service = service;
        }

        public CharacterIdentityStepPlanService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"identity-plan-{Guid.NewGuid():N}.db");
            var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
            var repository = new CharacterIdentityBuildRepository(options);
            await repository.EnsureSchemaAsync();
            return new Fixture(dbPath, new CharacterIdentityStepPlanService(repository));
        }

        /// <summary>
        /// Adds one plan row directly, so a test can build a plan the app cannot run.
        /// </summary>
        public async Task InsertPlanRowAsync(
            CharacterIdentityTargetKind kind, int order, CharacterIdentityBuildStep step, string handlerKey)
        {
            await using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO CharacterIdentityStepPlans (Kind, Step, OrderIndex, HandlerKey, TemplateKey)
                VALUES ($kind, $step, $orderIndex, $handlerKey, NULL);
                """;
            command.Parameters.AddWithValue("$kind", (int)kind);
            command.Parameters.AddWithValue("$step", (int)step);
            command.Parameters.AddWithValue("$orderIndex", order);
            command.Parameters.AddWithValue("$handlerKey", handlerKey);
            await command.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
    }
}
