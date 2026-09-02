using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.CorpusRunner;

internal static class CorpusCaseMapper
{
    public static RolePlaySession CreateSession(FrozenCorpusCase source) => new()
    {
        Id = source.Session.Id,
        ScenarioId = source.Session.ScenarioId,
        PersonaCharacterId = source.Session.PersonaCharacterId,
        PersonaName = source.Session.PersonaName,
        PersonaRole = source.Session.PersonaRole,
        PersonaGender = source.Session.PersonaGender,
        PersonaDescription = source.Session.PersonaDescription,
        Interactions = source.Session.Interactions.Select(CreateInteraction).ToList()
    };

    public static RolePlayTurn CreateTurn(FrozenCorpusCase source) => new()
    {
        TurnId = source.Turn.Id,
        SessionId = source.Session.Id,
        TurnIndex = source.Turn.Index,
        TurnKind = source.Turn.Kind,
        TriggerSource = source.Turn.TriggerSource,
        InputInteractionId = source.Turn.InputInteractionId,
        OutputInteractionIds = source.Turn.OutputInteractionIds,
        StartedUtc = source.Turn.StartedUtc,
        CompletedUtc = source.Turn.CompletedUtc,
        Status = RolePlayTurnStatus.Completed
    };

    public static IReadOnlyList<Character> CreateCharacters(FrozenCorpusCase source) => source.Characters.Select(item => new Character
    {
        Id = item.Id,
        Name = item.Name,
        Role = item.Role,
        Gender = item.Gender,
        Description = item.Description
    }).ToList();

    public static FullTurnContext CreateFullTurn(FrozenCorpusCase source)
    {
        var session = CreateSession(source);
        var turn = CreateTurn(source);
        var membership = turn.OutputInteractionIds.Prepend(turn.InputInteractionId).ToHashSet(StringComparer.Ordinal);
        var interactions = session.Interactions.Where(item => membership.Contains(item.Id)).ToList();
        var narrative = interactions.SingleOrDefault(item => item.ActorName.Equals("Narrative", StringComparison.OrdinalIgnoreCase));
        return new FullTurnContext
        {
            Turn = turn,
            Interactions = interactions,
            SelectedInteraction = narrative ?? interactions[0],
            NarrativeInteraction = narrative
        };
    }

    private static RolePlayInteraction CreateInteraction(FrozenInteraction source)
    {
        if (!Enum.TryParse<InteractionType>(source.InteractionType, false, out var interactionType))
            throw new CorpusValidationException("corpus_interaction_type_invalid", $"Interaction '{source.Id}' has invalid type '{source.InteractionType}'.");
        return new RolePlayInteraction
        {
            Id = source.Id,
            ActorName = source.ActorName,
            InteractionType = interactionType,
            Content = source.Content,
            CreatedAt = source.CreatedUtc
        };
    }
}