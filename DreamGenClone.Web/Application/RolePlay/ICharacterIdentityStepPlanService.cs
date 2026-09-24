using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The one place a target kind's step plan is resolved. The build machinery, the step handlers and the studio
/// UI all read the pipeline through this service, so the persisted plan is the single source of a build's
/// steps, their order, their handler and their prompt template.
/// </summary>
public interface ICharacterIdentityStepPlanService
{
    /// <summary>
    /// The plan for one target kind. Fails fast naming the kind when none is seeded, when a step is repeated or
    /// when a row names a handler this app does not implement.
    /// </summary>
    Task<CharacterIdentityStepPlan> GetPlanAsync(
        CharacterIdentityTargetKind kind, CancellationToken cancellationToken = default);
}
