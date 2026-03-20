using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayIdentityOptionsService : IRolePlayIdentityOptionsService
{
    private readonly IScenarioService _scenarioService;
    private readonly IBehaviorModeService _behaviorModeService;

    public RolePlayIdentityOptionsService(IScenarioService scenarioService, IBehaviorModeService behaviorModeService)
    {
        _scenarioService = scenarioService;
        _behaviorModeService = behaviorModeService;
    }

    public async Task<IReadOnlyList<IdentityOption>> GetIdentityOptionsAsync(RolePlaySession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var options = new List<IdentityOption>();
        var allowedActors = _behaviorModeService.GetAllowedActors(session.BehaviorMode).ToHashSet();

        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null)
            {
                foreach (var character in scenario.Characters)
                {
                    if (string.IsNullOrWhiteSpace(character.Name))
                    {
                        continue;
                    }

                    options.Add(new IdentityOption
                    {
                        Id = $"scene:{character.Id}",
                        DisplayName = character.Name.Trim(),
                        SourceType = IdentityOptionSource.SceneCharacter,
                        Actor = ContinueAsActor.Npc,
                        IsAvailable = allowedActors.Contains(ContinueAsActor.Npc),
                        AvailabilityReason = allowedActors.Contains(ContinueAsActor.Npc) ? null : $"Not allowed in {session.BehaviorMode} mode."
                    });
                }
            }
        }

        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();
        options.Add(new IdentityOption
        {
            Id = "persona:you",
            DisplayName = personaName,
            SourceType = IdentityOptionSource.Persona,
            Actor = ContinueAsActor.You,
            IsAvailable = allowedActors.Contains(ContinueAsActor.You),
            AvailabilityReason = allowedActors.Contains(ContinueAsActor.You) ? null : $"Not allowed in {session.BehaviorMode} mode."
        });

        options.Add(new IdentityOption
        {
            Id = "custom:adhoc",
            DisplayName = "Custom Character",
            SourceType = IdentityOptionSource.CustomCharacter,
            Actor = ContinueAsActor.Custom,
            IsAvailable = allowedActors.Contains(ContinueAsActor.Custom),
            AvailabilityReason = allowedActors.Contains(ContinueAsActor.Custom) ? null : $"Not allowed in {session.BehaviorMode} mode."
        });

        return options;
    }
}
