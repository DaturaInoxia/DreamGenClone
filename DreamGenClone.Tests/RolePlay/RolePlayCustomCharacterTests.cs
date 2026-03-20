using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayCustomCharacterTests
{
    [Fact]
    public async Task GetIdentityOptions_AlwaysIncludesCustomCharacterOption()
    {
        var session = new RolePlaySession();
        var service = CreateService();

        var options = await service.GetIdentityOptionsAsync(session);

        Assert.Contains(options, o => o.SourceType == IdentityOptionSource.CustomCharacter);
    }

    [Fact]
    public async Task GetIdentityOptions_CustomCharacter_HasCustomActor()
    {
        var session = new RolePlaySession();
        var service = CreateService();

        var options = await service.GetIdentityOptionsAsync(session);

        var custom = options.Single(o => o.SourceType == IdentityOptionSource.CustomCharacter);
        Assert.Equal(ContinueAsActor.Custom, custom.Actor);
    }

    [Fact]
    public async Task GetIdentityOptions_TakeTurnsMode_CustomCharacterIsAvailable()
    {
        var session = new RolePlaySession { BehaviorMode = BehaviorMode.TakeTurns };
        var service = CreateService();

        var options = await service.GetIdentityOptionsAsync(session);

        var custom = options.Single(o => o.SourceType == IdentityOptionSource.CustomCharacter);
        Assert.True(custom.IsAvailable);
    }

    [Fact]
    public async Task GetIdentityOptions_NpcOnlyMode_CustomCharacterNotAvailable()
    {
        var session = new RolePlaySession { BehaviorMode = BehaviorMode.NpcOnly };
        var service = CreateService();

        var options = await service.GetIdentityOptionsAsync(session);

        var custom = options.Single(o => o.SourceType == IdentityOptionSource.CustomCharacter);
        Assert.False(custom.IsAvailable);
        Assert.NotNull(custom.AvailabilityReason);
    }

    [Fact]
    public void UnifiedSubmission_CustomWithName_IsValid()
    {
        var submission = new UnifiedPromptSubmission
        {
            SessionId = "s1",
            PromptText = "Say hello.",
            Intent = PromptIntent.Message,
            SelectedIdentityId = "custom:adhoc",
            SelectedIdentityType = IdentityOptionSource.CustomCharacter,
            CustomIdentityName = "Ghost Rider"
        };

        var result = submission.IsValid(out var error);

        Assert.True(result);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void UnifiedSubmission_CustomWithPrefixedId_IsValidWithoutCustomName()
    {
        var submission = new UnifiedPromptSubmission
        {
            SessionId = "s1",
            PromptText = "Say hello.",
            Intent = PromptIntent.Message,
            SelectedIdentityId = "custom:adhoc",      // starts with "custom:" → valid
            SelectedIdentityType = IdentityOptionSource.CustomCharacter,
            CustomIdentityName = null
        };

        var result = submission.IsValid(out var error);

        Assert.True(result);
        Assert.Equal(string.Empty, error);
    }

    private static RolePlayIdentityOptionsService CreateService()
    {
        var scenarioService = new NullScenarioService();
        var behaviorMode = new BehaviorModeService(NullLogger<BehaviorModeService>.Instance);
        return new RolePlayIdentityOptionsService(scenarioService, behaviorMode);
    }

    private sealed class NullScenarioService : IScenarioService
    {
        public Task<Scenario?> GetScenarioAsync(string id) => Task.FromResult<Scenario?>(null);
        public Task<Scenario> CreateScenarioAsync(string name, string? description = null) => throw new NotImplementedException();
        public Task<List<Scenario>> GetAllScenariosAsync() => throw new NotImplementedException();
        public Task<Scenario> SaveScenarioAsync(Scenario scenario) => throw new NotImplementedException();
        public Task<bool> DeleteScenarioAsync(string id) => throw new NotImplementedException();
        public Task<Scenario> CloneScenarioAsync(string id, string newName) => throw new NotImplementedException();
    }
}
