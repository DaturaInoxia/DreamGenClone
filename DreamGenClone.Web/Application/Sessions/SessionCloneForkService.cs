using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Sessions;

public sealed class SessionCloneForkService : ISessionCloneForkService
{
    private readonly ISessionService _sessionService;
    private readonly ILogger<SessionCloneForkService> _logger;

    public SessionCloneForkService(ISessionService sessionService, ILogger<SessionCloneForkService> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task<string?> CloneAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var story = await _sessionService.LoadStorySessionAsync(sessionId, cancellationToken);
        if (story is not null)
        {
            var clone = new StorySession
            {
                Title = $"{story.Title} (Clone)",
                ScenarioId = story.ScenarioId,
                Blocks = story.Blocks.Select(CloneBlock).ToList()
            };

            await _sessionService.SaveStorySessionAsync(clone, cancellationToken);
            _logger.LogInformation("Story session cloned: {SourceId} -> {CloneId}", sessionId, clone.Id);
            return clone.Id;
        }

        var rolePlay = await _sessionService.LoadRolePlaySessionAsync(sessionId, cancellationToken);
        if (rolePlay is null)
        {
            return null;
        }

        var rpClone = new RolePlaySession
        {
            Title = $"{rolePlay.Title} (Clone)",
            ScenarioId = rolePlay.ScenarioId,
            BehaviorMode = rolePlay.BehaviorMode,
            ParentSessionId = rolePlay.ParentSessionId,
            PersonaName = rolePlay.PersonaName,
            PersonaDescription = rolePlay.PersonaDescription,
            PersonaTemplateId = rolePlay.PersonaTemplateId,
            PersonaPerspectiveMode = rolePlay.PersonaPerspectiveMode,
            CharacterPerspectives = rolePlay.CharacterPerspectives.Select(ClonePerspective).ToList(),
            Interactions = rolePlay.Interactions.Select(CloneInteraction).ToList()
        };

        await _sessionService.SaveRolePlaySessionAsync(rpClone, cancellationToken);
        _logger.LogInformation("Role-play session cloned: {SourceId} -> {CloneId}", sessionId, rpClone.Id);
        return rpClone.Id;
    }

    public async Task<string?> ForkAsync(string sessionId, int fromIndexInclusive, CancellationToken cancellationToken = default)
    {
        var story = await _sessionService.LoadStorySessionAsync(sessionId, cancellationToken);
        if (story is not null)
        {
            var bounded = Math.Min(fromIndexInclusive, story.Blocks.Count - 1);
            if (bounded < 0)
            {
                bounded = -1;
            }

            var fork = new StorySession
            {
                Title = $"{story.Title} (Fork)",
                ScenarioId = story.ScenarioId,
                Blocks = bounded >= 0 ? story.Blocks.Take(bounded + 1).Select(CloneBlock).ToList() : []
            };

            await _sessionService.SaveStorySessionAsync(fork, cancellationToken);
            _logger.LogInformation("Story session forked: {SourceId} -> {ForkId} from index {Index}", sessionId, fork.Id, fromIndexInclusive);
            return fork.Id;
        }

        var rolePlay = await _sessionService.LoadRolePlaySessionAsync(sessionId, cancellationToken);
        if (rolePlay is null)
        {
            return null;
        }

        var rpBounded = Math.Min(fromIndexInclusive, rolePlay.Interactions.Count - 1);
        if (rpBounded < 0)
        {
            rpBounded = -1;
        }

        var rpFork = new RolePlaySession
        {
            Title = $"{rolePlay.Title} (Fork)",
            ScenarioId = rolePlay.ScenarioId,
            BehaviorMode = rolePlay.BehaviorMode,
            ParentSessionId = rolePlay.Id,
            PersonaName = rolePlay.PersonaName,
            PersonaDescription = rolePlay.PersonaDescription,
            PersonaTemplateId = rolePlay.PersonaTemplateId,
            PersonaPerspectiveMode = rolePlay.PersonaPerspectiveMode,
            CharacterPerspectives = rolePlay.CharacterPerspectives.Select(ClonePerspective).ToList(),
            Interactions = rpBounded >= 0
                ? rolePlay.Interactions.Take(rpBounded + 1).Select(CloneInteraction).ToList()
                : []
        };

        await _sessionService.SaveRolePlaySessionAsync(rpFork, cancellationToken);
        _logger.LogInformation("Role-play session forked: {SourceId} -> {ForkId} from index {Index}", sessionId, rpFork.Id, fromIndexInclusive);
        return rpFork.Id;
    }

    private static StoryBlock CloneBlock(StoryBlock block)
    {
        return new StoryBlock
        {
            BlockType = block.BlockType,
            Author = block.Author,
            Content = block.Content,
            CreatedAt = block.CreatedAt
        };
    }

    private static RolePlayInteraction CloneInteraction(RolePlayInteraction interaction)
    {
        return new RolePlayInteraction
        {
            InteractionType = interaction.InteractionType,
            ActorName = interaction.ActorName,
            Content = interaction.Content,
            CreatedAt = interaction.CreatedAt
        };
    }

    private static RolePlayCharacterPerspective ClonePerspective(RolePlayCharacterPerspective perspective)
    {
        return new RolePlayCharacterPerspective
        {
            CharacterId = perspective.CharacterId,
            CharacterName = perspective.CharacterName,
            PerspectiveMode = perspective.PerspectiveMode
        };
    }
}
